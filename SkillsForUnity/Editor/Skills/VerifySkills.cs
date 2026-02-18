using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnitySkills
{
    /// <summary>
    /// Verify skills - run a full verification pipeline (compile, test, validate) in a single async job.
    /// Uses a start/poll pattern: verify_start returns a jobId, verify_get_result polls for completion.
    /// Job state is persisted via SessionState to survive domain reloads (Play Mode transitions).
    /// </summary>
    public static class VerifySkills
    {
        // ─────────────────────────────────────────────────────────────
        //  Constants
        // ─────────────────────────────────────────────────────────────

        private const string ActiveJobsKey = "VerifySkills_ActiveJobs";
        private const string JobKeyPrefix = "VerifySkills_Job_";

        // In-memory cache (rebuilt from SessionState after domain reload)
        private static readonly Dictionary<string, VerifyJob> _jobs = new Dictionary<string, VerifyJob>();
        private static readonly Dictionary<string, EditorApplication.CallbackFunction> _updateCallbacks
            = new Dictionary<string, EditorApplication.CallbackFunction>();

        // ─────────────────────────────────────────────────────────────
        //  Data models
        // ─────────────────────────────────────────────────────────────

        internal enum VerifyStep { Idle, Compilation, Errors, Tests, Scene, UI, Done }

        internal enum UIPhase { NotStarted, EnteringPlay, WaitingInit, Querying, ExitingPlay, Done }

        internal class VerifyJob
        {
            public string JobId;
            public string Status = "running";  // running | completed | error
            public VerifyStep CurrentStep = VerifyStep.Idle;
            public long StartTimeTicks;
            public double Duration;

            // Parameters
            public bool WaitForCompilation = true;
            public int CompilationTimeout = 60;
            public bool RunTests = true;
            public string TestMode = "EditMode";
            public string TestFilter = "";
            public int TestTimeout = 120;
            public bool ValidateScene = true;
            public bool UICheckEnabled = false;
            public int UICheckDepth = 2;
            public string[] UICheckRequiredNames = Array.Empty<string>();
            public int UICheckInitWaitSeconds = 8;

            // Step results - stored as JObject for JSON serialization
            public Dictionary<string, JObject> Steps = new Dictionary<string, JObject>();
            public string Summary = "";

            // Internal state
            public string TestJobId;
            public bool CompilationWaitStarted;
            public long StepStartTimeTicks;
            public bool Failed;

            // UI phase tracking (persisted as part of job)
            public UIPhase CurrentUIPhase = UIPhase.NotStarted;
            public long UIWaitStartTicks;

            // Helper properties (not serialized, computed from ticks)
            [JsonIgnore]
            public DateTime StartTime
            {
                get => new DateTime(StartTimeTicks);
                set => StartTimeTicks = value.Ticks;
            }

            [JsonIgnore]
            public DateTime StepStartTime
            {
                get => new DateTime(StepStartTimeTicks);
                set => StepStartTimeTicks = value.Ticks;
            }

            [JsonIgnore]
            public DateTime UIWaitStart
            {
                get => new DateTime(UIWaitStartTicks);
                set => UIWaitStartTicks = value.Ticks;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  SessionState persistence
        // ─────────────────────────────────────────────────────────────

        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            StringEscapeHandling = StringEscapeHandling.Default,
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>
        /// Save a job to SessionState and update active jobs list.
        /// </summary>
        private static void PersistJob(VerifyJob job)
        {
            var json = JsonConvert.SerializeObject(job, _jsonSettings);
            SessionState.SetString(JobKeyPrefix + job.JobId, json);

            // Ensure job is in active list
            var activeIds = GetActiveJobIds();
            if (!activeIds.Contains(job.JobId))
            {
                activeIds.Add(job.JobId);
                SessionState.SetString(ActiveJobsKey, JsonConvert.SerializeObject(activeIds));
            }
        }

        /// <summary>
        /// Load a job from SessionState.
        /// </summary>
        private static VerifyJob LoadJob(string jobId)
        {
            var json = SessionState.GetString(JobKeyPrefix + jobId, "");
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                return JsonConvert.DeserializeObject<VerifyJob>(json, _jsonSettings);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Remove a job from SessionState and active list.
        /// </summary>
        private static void RemovePersistedJob(string jobId)
        {
            SessionState.EraseString(JobKeyPrefix + jobId);
            var activeIds = GetActiveJobIds();
            activeIds.Remove(jobId);
            SessionState.SetString(ActiveJobsKey, JsonConvert.SerializeObject(activeIds));
        }

        /// <summary>
        /// Get list of active job IDs from SessionState.
        /// </summary>
        private static List<string> GetActiveJobIds()
        {
            var json = SessionState.GetString(ActiveJobsKey, "[]");
            try
            {
                return JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Get a job from in-memory cache, falling back to SessionState.
        /// </summary>
        private static VerifyJob GetOrLoadJob(string jobId)
        {
            if (_jobs.TryGetValue(jobId, out var cached))
                return cached;

            var loaded = LoadJob(jobId);
            if (loaded != null)
                _jobs[jobId] = loaded;
            return loaded;
        }

        // ─────────────────────────────────────────────────────────────
        //  Domain reload recovery via [InitializeOnLoadMethod]
        // ─────────────────────────────────────────────────────────────

        [InitializeOnLoadMethod]
        private static void OnDomainReload()
        {
            // Clear in-memory state (it's stale after domain reload)
            _jobs.Clear();
            _updateCallbacks.Clear();

            // Restore running jobs from SessionState
            var activeIds = GetActiveJobIds();
            foreach (var jobId in activeIds.ToList())
            {
                var job = LoadJob(jobId);
                if (job == null)
                {
                    // Stale entry, remove
                    RemovePersistedJob(jobId);
                    continue;
                }

                if (job.Status != "running")
                {
                    // Job already finished, keep in SessionState for polling but don't register callback
                    _jobs[jobId] = job;
                    continue;
                }

                // Job is still running - put in cache and re-register update callback
                _jobs[jobId] = job;
                RegisterPipelineCallback(jobId);
                SkillsLogger.Log($"[Verify] Restored running job {jobId} at step {job.CurrentStep} after domain reload");
            }
        }

        /// <summary>
        /// Register an EditorApplication.update callback for a job.
        /// </summary>
        private static void RegisterPipelineCallback(string jobId)
        {
            // Remove existing callback if any
            if (_updateCallbacks.TryGetValue(jobId, out var existing))
                EditorApplication.update -= existing;

            EditorApplication.CallbackFunction callback = null;
            callback = () => RunPipeline(jobId, callback);
            _updateCallbacks[jobId] = callback;
            EditorApplication.update += callback;
        }

        /// <summary>
        /// Unregister the update callback for a job.
        /// </summary>
        private static void UnregisterPipelineCallback(string jobId)
        {
            if (_updateCallbacks.TryGetValue(jobId, out var callback))
            {
                EditorApplication.update -= callback;
                _updateCallbacks.Remove(jobId);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Skill: verify_start
        // ─────────────────────────────────────────────────────────────

        [UnitySkill("verify_start", "Start a verification pipeline (compile check, tests, scene validation, UI check). Returns a jobId for polling via verify_get_result.")]
        public static object VerifyStart(
            bool waitForCompilation = true,
            int compilationTimeout = 60,
            bool runTests = true,
            string testMode = "EditMode",
            string testFilter = "",
            int testTimeout = 120,
            bool validateScene = true,
            bool uiCheckEnabled = false,
            int uiCheckDepth = 2,
            string uiCheckRequiredNames = "",
            int uiCheckInitWaitSeconds = 8)
        {
            // Validate parameters
            if (compilationTimeout < 1 || compilationTimeout > 300)
                return new { error = "compilationTimeout must be between 1 and 300" };
            if (testTimeout < 1 || testTimeout > 600)
                return new { error = "testTimeout must be between 1 and 600" };
            if (uiCheckDepth < 1 || uiCheckDepth > 10)
                return new { error = "uiCheckDepth must be between 1 and 10" };
            if (uiCheckInitWaitSeconds < 1 || uiCheckInitWaitSeconds > 60)
                return new { error = "uiCheckInitWaitSeconds must be between 1 and 60" };

            var jobId = "verify-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var job = new VerifyJob
            {
                JobId = jobId,
                StartTime = DateTime.Now,
                WaitForCompilation = waitForCompilation,
                CompilationTimeout = compilationTimeout,
                RunTests = runTests,
                TestMode = testMode,
                TestFilter = testFilter ?? "",
                TestTimeout = testTimeout,
                ValidateScene = validateScene,
                UICheckEnabled = uiCheckEnabled,
                UICheckDepth = uiCheckDepth,
                UICheckRequiredNames = string.IsNullOrEmpty(uiCheckRequiredNames)
                    ? Array.Empty<string>()
                    : uiCheckRequiredNames.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray(),
                UICheckInitWaitSeconds = uiCheckInitWaitSeconds
            };

            _jobs[jobId] = job;
            PersistJob(job);
            RegisterPipelineCallback(jobId);

            return new { jobId, message = "Verification started. Poll verify_get_result for status." };
        }

        // ─────────────────────────────────────────────────────────────
        //  Skill: verify_get_result
        // ─────────────────────────────────────────────────────────────

        [UnitySkill("verify_get_result", "Get the result of a verification job started by verify_start.")]
        public static object VerifyGetResult(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
                return new { error = "jobId is required" };

            var job = GetOrLoadJob(jobId);
            if (job == null)
                return new { error = $"Verify job not found: {jobId}" };

            // Convert Steps from Dictionary<string, JObject> to a plain object for output
            var stepsOutput = new Dictionary<string, object>();
            foreach (var kv in job.Steps)
            {
                if (!kv.Key.StartsWith("_"))
                    stepsOutput[kv.Key] = kv.Value;
            }

            if (job.Status == "running")
            {
                return new
                {
                    jobId,
                    status = "running",
                    currentStep = job.CurrentStep.ToString().ToLower(),
                    elapsedSeconds = (DateTime.Now - job.StartTime).TotalSeconds,
                    steps = stepsOutput
                };
            }

            // Completed or error
            return new
            {
                jobId,
                status = job.Status,
                duration = job.Duration,
                steps = stepsOutput,
                summary = job.Summary
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Pipeline runner (called from EditorApplication.update)
        // ─────────────────────────────────────────────────────────────

        private static void RunPipeline(string jobId, EditorApplication.CallbackFunction callback)
        {
            var job = GetOrLoadJob(jobId);
            if (job == null)
            {
                UnregisterPipelineCallback(jobId);
                return;
            }

            if (job.Status != "running")
            {
                UnregisterPipelineCallback(jobId);
                return;
            }

            try
            {
                switch (job.CurrentStep)
                {
                    case VerifyStep.Idle:
                        job.CurrentStep = VerifyStep.Compilation;
                        job.StepStartTime = DateTime.Now;
                        break;

                    case VerifyStep.Compilation:
                        if (ExecuteCompilationStep(job))
                            job.CurrentStep = VerifyStep.Errors;
                        break;

                    case VerifyStep.Errors:
                        ExecuteErrorCheckStep(job);
                        job.CurrentStep = VerifyStep.Tests;
                        break;

                    case VerifyStep.Tests:
                        if (ExecuteTestStep(job))
                            job.CurrentStep = VerifyStep.Scene;
                        break;

                    case VerifyStep.Scene:
                        ExecuteSceneStep(job);
                        job.CurrentStep = VerifyStep.UI;
                        break;

                    case VerifyStep.UI:
                        if (ExecuteUIStep(job))
                            job.CurrentStep = VerifyStep.Done;
                        break;

                    case VerifyStep.Done:
                        FinishJob(job);
                        break;
                }

                // Persist after every step change
                PersistJob(job);
            }
            catch (Exception ex)
            {
                job.Status = "error";
                job.Duration = (DateTime.Now - job.StartTime).TotalSeconds;
                job.Summary = $"Pipeline error: {ex.Message}";
                PersistJob(job);
                SkillsLogger.LogError($"Verify pipeline error: {ex}");
            }
        }

        private static void FinishJob(VerifyJob job)
        {
            job.Status = "completed";
            job.Duration = (DateTime.Now - job.StartTime).TotalSeconds;
            job.Summary = job.Failed ? BuildFailureSummary(job) : "All checks passed";
        }

        private static string BuildFailureSummary(VerifyJob job)
        {
            var parts = new List<string>();

            foreach (var kv in job.Steps)
            {
                if (kv.Key.StartsWith("_")) continue;

                var step = kv.Value;
                if (step == null) continue;

                var passedToken = step["passed"];
                if (passedToken != null && passedToken.Type == JTokenType.Boolean && !(bool)passedToken)
                {
                    if (kv.Key == "errors" && step["count"] != null)
                        parts.Add($"Compilation errors ({step["count"]})");
                    else if (kv.Key == "tests" && step["failed"] != null)
                        parts.Add($"Test failures ({step["failed"]})");
                    else if (kv.Key == "scene" && step["totalIssues"] != null)
                        parts.Add($"Scene issues ({step["totalIssues"]})");
                    else if (kv.Key == "ui" && step["missingLayers"] is JArray ml)
                        parts.Add($"Missing UI layers ({string.Join(", ", ml.Select(t => t.ToString()))})");
                    else
                        parts.Add($"{kv.Key} failed");
                }
            }

            return parts.Count > 0 ? string.Join("; ", parts) : "Unknown failure";
        }

        // ─────────────────────────────────────────────────────────────
        //  Helper: Create a JObject from key-value pairs
        // ─────────────────────────────────────────────────────────────

        private static JObject StepResult(params (string key, object value)[] pairs)
        {
            var obj = new JObject();
            foreach (var (key, value) in pairs)
            {
                obj[key] = value != null ? JToken.FromObject(value) : JValue.CreateNull();
            }
            return obj;
        }

        // ─────────────────────────────────────────────────────────────
        //  Step 1: Wait for compilation
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Execute the compilation wait step. Checks if Unity is currently compiling
        /// and waits for it to finish. Non-blocking: returns true if step is complete,
        /// false if still waiting (caller should call again on next update).
        /// </summary>
        internal static bool ExecuteCompilationStep(VerifyJob job)
        {
            if (!job.WaitForCompilation)
            {
                job.Steps["compilation"] = StepResult(
                    ("skipped", true),
                    ("reason", "waitForCompilation is false"));
                return true;
            }

            if (!job.CompilationWaitStarted)
            {
                job.StepStartTime = DateTime.Now;
                job.CompilationWaitStarted = true;
            }

            var elapsed = (DateTime.Now - job.StepStartTime).TotalSeconds;

            if (EditorApplication.isCompiling)
            {
                if (elapsed > job.CompilationTimeout)
                {
                    job.Steps["compilation"] = StepResult(
                        ("passed", false),
                        ("duration", elapsed),
                        ("error", $"Compilation timed out after {job.CompilationTimeout}s"));
                    job.Failed = true;
                    return true;
                }
                return false;
            }

            job.Steps["compilation"] = StepResult(
                ("passed", true),
                ("duration", elapsed));
            return true;
        }

        // ─────────────────────────────────────────────────────────────
        //  Step 2: Check compilation errors
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Check for compilation errors using DebugSkills.DebugGetErrors.
        /// Sets job.Failed = true if errors are found.
        /// </summary>
        internal static void ExecuteErrorCheckStep(VerifyJob job)
        {
            if (job.Failed)
            {
                job.Steps["errors"] = StepResult(
                    ("skipped", true),
                    ("reason", "Previous step failed"));
                return;
            }

            try
            {
                var result = DebugSkills.DebugGetErrors(limit: 50);

                var countProp = result.GetType().GetProperty("count");
                var logsProp = result.GetType().GetProperty("logs");
                int errorCount = countProp != null ? (int)countProp.GetValue(result) : 0;

                if (errorCount > 0)
                {
                    var errors = new List<string>();
                    if (logsProp?.GetValue(result) is System.Collections.IEnumerable logs)
                    {
                        foreach (var log in logs)
                        {
                            var msgProp = log.GetType().GetProperty("message");
                            if (msgProp != null)
                                errors.Add(msgProp.GetValue(log)?.ToString() ?? "");
                        }
                    }

                    job.Steps["errors"] = StepResult(
                        ("passed", false),
                        ("count", errorCount),
                        ("errors", errors.ToArray()));
                    job.Failed = true;
                }
                else
                {
                    job.Steps["errors"] = StepResult(
                        ("passed", true),
                        ("count", 0));
                }
            }
            catch (Exception ex)
            {
                job.Steps["errors"] = StepResult(
                    ("passed", false),
                    ("count", -1),
                    ("error", $"Failed to check errors: {ex.Message}"));
                job.Failed = true;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Step 3: Run tests
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Execute the test run step. Uses TestSkills.TestRun/TestGetResult internally.
        /// Returns true when complete, false when still running (non-blocking).
        /// </summary>
        internal static bool ExecuteTestStep(VerifyJob job)
        {
            if (!job.RunTests)
            {
                job.Steps["tests"] = StepResult(
                    ("skipped", true),
                    ("reason", "runTests is false"));
                return true;
            }

            if (job.Failed)
            {
                job.Steps["tests"] = StepResult(
                    ("skipped", true),
                    ("reason", "Previous step failed, skipping tests"));
                return true;
            }

            // Start tests if not yet started
            if (string.IsNullOrEmpty(job.TestJobId))
            {
                job.StepStartTime = DateTime.Now;
                var startResult = TestSkills.TestRun(job.TestMode, string.IsNullOrEmpty(job.TestFilter) ? null : job.TestFilter);

                var jobIdProp = startResult.GetType().GetProperty("jobId");
                if (jobIdProp == null)
                {
                    job.Steps["tests"] = StepResult(
                        ("passed", false),
                        ("error", "Failed to start test run"));
                    job.Failed = true;
                    return true;
                }

                job.TestJobId = jobIdProp.GetValue(startResult)?.ToString();
                return false;
            }

            var elapsed = (DateTime.Now - job.StepStartTime).TotalSeconds;
            if (elapsed > job.TestTimeout)
            {
                job.Steps["tests"] = StepResult(
                    ("passed", false),
                    ("error", $"Tests timed out after {job.TestTimeout}s"));
                job.Failed = true;
                return true;
            }

            var pollResult = TestSkills.TestGetResult(job.TestJobId);
            var statusProp = pollResult.GetType().GetProperty("status");
            var status = statusProp?.GetValue(pollResult)?.ToString();

            if (status != "completed")
            {
                var totalProp = pollResult.GetType().GetProperty("totalTests");
                var passedProp = pollResult.GetType().GetProperty("passedTests");
                int total = totalProp != null ? (int)totalProp.GetValue(pollResult) : 0;
                int passed = passedProp != null ? (int)passedProp.GetValue(pollResult) : 0;
                job.Steps["tests"] = StepResult(
                    ("running", true),
                    ("progress", $"{passed}/{total}"));
                return false;
            }

            var totalFinal = (int)pollResult.GetType().GetProperty("totalTests").GetValue(pollResult);
            var passedFinal = (int)pollResult.GetType().GetProperty("passedTests").GetValue(pollResult);
            var failedFinal = (int)pollResult.GetType().GetProperty("failedTests").GetValue(pollResult);
            var failedNames = pollResult.GetType().GetProperty("failedTestNames")?.GetValue(pollResult) as string[] ?? Array.Empty<string>();
            var testDuration = elapsed;

            var testsPassed = failedFinal == 0;
            job.Steps["tests"] = StepResult(
                ("passed", testsPassed),
                ("total", totalFinal),
                ("failed", failedFinal),
                ("skipped", totalFinal - passedFinal - failedFinal),
                ("duration", testDuration),
                ("failures", failedNames));

            if (!testsPassed)
                job.Failed = true;

            return true;
        }

        // ─────────────────────────────────────────────────────────────
        //  Step 4: Scene validation
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Execute scene validation. Runs even if tests failed, but skips if compilation had errors.
        /// </summary>
        internal static void ExecuteSceneStep(VerifyJob job)
        {
            if (!job.ValidateScene)
            {
                job.Steps["scene"] = StepResult(
                    ("skipped", true),
                    ("reason", "validateScene is false"));
                return;
            }

            // Skip if compilation errors exist
            if (job.Steps.TryGetValue("errors", out var errStep))
            {
                var errPassed = errStep["passed"];
                if (errPassed != null && errPassed.Type == JTokenType.Boolean && !(bool)errPassed)
                {
                    job.Steps["scene"] = StepResult(
                        ("skipped", true),
                        ("reason", "Compilation had errors, skipping scene validation"));
                    return;
                }
            }

            try
            {
                var result = ValidationSkills.ValidateScene();

                var totalProp = result.GetType().GetProperty("totalIssues");
                int totalIssues = totalProp != null ? (int)totalProp.GetValue(result) : 0;

                var issues = new List<string>();
                var issuesProp = result.GetType().GetProperty("issues");
                if (issuesProp?.GetValue(result) is System.Collections.IEnumerable issueList)
                {
                    foreach (var issue in issueList)
                    {
                        var sevProp = issue.GetType().GetProperty("severity");
                        var msgProp = issue.GetType().GetProperty("message");
                        var goProp = issue.GetType().GetProperty("gameObject");
                        var sev = sevProp?.GetValue(issue)?.ToString() ?? "";
                        var msg = msgProp?.GetValue(issue)?.ToString() ?? "";
                        var go = goProp?.GetValue(issue)?.ToString() ?? "";
                        issues.Add($"[{sev}] {go}: {msg}");
                    }
                }

                bool hasErrors = false;
                var summaryProp = result.GetType().GetProperty("summary");
                if (summaryProp?.GetValue(result) is object summary)
                {
                    var errCountProp = summary.GetType().GetProperty("errors");
                    if (errCountProp != null)
                        hasErrors = (int)errCountProp.GetValue(summary) > 0;
                }

                job.Steps["scene"] = StepResult(
                    ("passed", !hasErrors),
                    ("totalIssues", totalIssues),
                    ("issues", issues.ToArray()));

                if (hasErrors)
                    job.Failed = true;
            }
            catch (Exception ex)
            {
                job.Steps["scene"] = StepResult(
                    ("passed", false),
                    ("error", $"Scene validation error: {ex.Message}"));
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Step 5: UI check (multi-phase async, survives domain reload)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Execute the UI check step. This is a multi-phase async step:
        /// 1. Enter Play Mode
        /// 2. Wait for initialization (survives domain reload via SessionState)
        /// 3. Query UI tree
        /// 4. Exit Play Mode
        /// Returns true when complete, false when still in progress.
        /// </summary>
        internal static bool ExecuteUIStep(VerifyJob job)
        {
            if (!job.UICheckEnabled)
            {
                job.Steps["ui"] = StepResult(
                    ("skipped", true),
                    ("reason", "uiCheck is disabled"));
                return true;
            }

            // Skip if compilation had errors
            if (job.Steps.TryGetValue("errors", out var errStep))
            {
                var errPassed = errStep["passed"];
                if (errPassed != null && errPassed.Type == JTokenType.Boolean && !(bool)errPassed)
                {
                    job.Steps["ui"] = StepResult(
                        ("skipped", true),
                        ("reason", "Compilation had errors, skipping UI check"));
                    return true;
                }
            }

            // Initialize UI phase if not started
            if (job.CurrentUIPhase == UIPhase.NotStarted)
            {
                job.StepStartTime = DateTime.Now;
                if (EditorApplication.isPlaying)
                {
                    job.CurrentUIPhase = UIPhase.WaitingInit;
                    job.UIWaitStart = DateTime.Now;
                }
                else
                {
                    job.CurrentUIPhase = UIPhase.EnteringPlay;
                    try
                    {
                        EditorApplication.isPlaying = true;
                    }
                    catch (Exception ex)
                    {
                        job.Steps["ui"] = StepResult(
                            ("skipped", true),
                            ("reason", $"Failed to enter Play Mode: {ex.Message}"));
                        job.CurrentUIPhase = UIPhase.Done;
                        return true;
                    }
                }
                return false;
            }

            var stepElapsed = (DateTime.Now - job.StepStartTime).TotalSeconds;

            // Timeout for entire UI step
            if (stepElapsed > job.UICheckInitWaitSeconds + 30)
            {
                if (EditorApplication.isPlaying)
                    EditorApplication.isPlaying = false;

                job.Steps["ui"] = StepResult(
                    ("skipped", true),
                    ("reason", $"UI check timed out after {stepElapsed:F1}s"));
                job.CurrentUIPhase = UIPhase.Done;
                return true;
            }

            switch (job.CurrentUIPhase)
            {
                case UIPhase.EnteringPlay:
                    if (EditorApplication.isPlaying)
                    {
                        job.CurrentUIPhase = UIPhase.WaitingInit;
                        job.UIWaitStart = DateTime.Now;
                    }
                    return false;

                case UIPhase.WaitingInit:
                    if ((DateTime.Now - job.UIWaitStart).TotalSeconds >= job.UICheckInitWaitSeconds)
                    {
                        job.CurrentUIPhase = UIPhase.Querying;
                    }
                    return false;

                case UIPhase.Querying:
                    try
                    {
                        var treeResult = UIToolkitSkills.Tree(depth: job.UICheckDepth);
                        var errorProp = treeResult.GetType().GetProperty("error");

                        if (errorProp?.GetValue(treeResult) != null)
                        {
                            job.Steps["ui"] = StepResult(
                                ("skipped", true),
                                ("reason", $"UI tree query failed: {errorProp.GetValue(treeResult)}"));
                        }
                        else
                        {
                            var treeProp = treeResult.GetType().GetProperty("tree");
                            var treeText = treeProp?.GetValue(treeResult)?.ToString() ?? "";

                            var foundLayers = new List<string>();
                            var missingLayers = new List<string>();

                            foreach (var name in job.UICheckRequiredNames)
                            {
                                if (treeText.Contains(name))
                                    foundLayers.Add(name);
                                else
                                    missingLayers.Add(name);
                            }

                            job.Steps["ui"] = StepResult(
                                ("passed", missingLayers.Count == 0),
                                ("foundLayers", foundLayers.ToArray()),
                                ("missingLayers", missingLayers.ToArray()));

                            if (missingLayers.Count > 0)
                                job.Failed = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        job.Steps["ui"] = StepResult(
                            ("skipped", true),
                            ("reason", $"UI check error: {ex.Message}"));
                    }

                    EditorApplication.isPlaying = false;
                    job.CurrentUIPhase = UIPhase.ExitingPlay;
                    return false;

                case UIPhase.ExitingPlay:
                    if (!EditorApplication.isPlaying)
                    {
                        job.CurrentUIPhase = UIPhase.Done;
                        return true;
                    }
                    return false;
            }

            return true;
        }

        // ─────────────────────────────────────────────────────────────
        //  Internal helpers (for testing)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Clear all jobs from memory and SessionState. Used in tests.
        /// </summary>
        internal static void ClearJobs()
        {
            foreach (var jobId in _jobs.Keys.ToList())
            {
                UnregisterPipelineCallback(jobId);
                RemovePersistedJob(jobId);
            }
            _jobs.Clear();
            _updateCallbacks.Clear();
            SessionState.SetString(ActiveJobsKey, "[]");
        }

        /// <summary>
        /// Get a job by ID. Used in tests.
        /// </summary>
        internal static VerifyJob GetJob(string jobId)
        {
            return GetOrLoadJob(jobId);
        }
    }
}
