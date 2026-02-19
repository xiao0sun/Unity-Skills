using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
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

        internal enum UIPhase { NotStarted, LoadingScene, EnteringPlay, WaitingInit, ClickingElement, WaitingForElement, Querying, ExitingPlay, Done }

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
            public string UICheckClickSelector = "";
            public string UICheckWaitSelector = "";

            // Step results - stored as JObject for JSON serialization
            public Dictionary<string, JObject> Steps = new Dictionary<string, JObject>();
            public string Summary = "";

            // Internal state
            public string TestJobId;
            public bool CompilationWaitStarted;
            public long StepStartTimeTicks;
            public bool Failed;

            // 测试完成后等待 Test Framework 清理的帧计数器
            // RestoreSceneSetupTask 在 EditorApplication.update 上异步清理，
            // 需要等待几帧让它完成，避免进入 Play Mode 时抛 InvalidOperationException
            public int TestCleanupFrames;

            // UI phase tracking (persisted as part of job)
            public UIPhase CurrentUIPhase = UIPhase.NotStarted;
            public long UIWaitStartTicks;
            public string UIScenePath = "";
            public string UIWaitJobId = "";  // WaitingForElement 阶段使用的等待 job ID

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
            int uiCheckInitWaitSeconds = 8,
            string uiScenePath = "",
            string uiCheckClickSelector = "",
            string uiCheckWaitSelector = "")
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
                UICheckInitWaitSeconds = uiCheckInitWaitSeconds,
                UIScenePath = uiScenePath ?? "",
                UICheckClickSelector = uiCheckClickSelector ?? "",
                UICheckWaitSelector = uiCheckWaitSelector ?? ""
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

            // 全局 Play Mode 守卫：只有 UI step 允许在 Play Mode 中运行
            // 其他步骤（Compilation/Errors/Tests/Scene）都需要 Edit Mode 环境
            // 域重载恢复后如果仍在 Play Mode 中，等待退出后再继续
            if (EditorApplication.isPlayingOrWillChangePlaymode && job.CurrentStep != VerifyStep.UI)
            {
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

                    // SceneStep 改为非阻塞等待（与 TestStep 行为一致）
                    case VerifyStep.Scene:
                        if (ExecuteSceneStep(job))
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
            // Edit Mode 测试不能在 Play Mode 中运行，先退出 Play Mode
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (EditorApplication.isPlaying)
                    EditorApplication.isPlaying = false;
                return false; // 等下一帧 Play Mode 完全退出后再继续
            }

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

            // 检测域重载后 test job 丢失的情况（TestSkills 内存状态不持久化）
            var errorProp = pollResult.GetType().GetProperty("error");
            if (errorProp != null && errorProp.GetValue(pollResult) != null)
            {
                SkillsLogger.LogWarning($"[Verify] Test job '{job.TestJobId}' lost (likely domain reload), restarting tests");
                job.TestJobId = null;  // 清空，下一帧重新启动
                return false;
            }

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

            // 测试完成后等待几帧，让 Test Framework 的 RestoreSceneSetupTask 完成清理。
            // 如果立即推进到下一步（Scene/UI）并进入 Play Mode，
            // RestoreSceneSetupTask 会因在 Play Mode 中执行而抛 InvalidOperationException。
            if (job.TestCleanupFrames < 10)
            {
                job.TestCleanupFrames++;
                return false;
            }

            return true;
        }

        // ─────────────────────────────────────────────────────────────
        //  Step 4: Scene validation
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Execute scene validation. Runs even if tests failed, but skips if compilation had errors.
        /// 返回 true 表示完成（可推进到下一步），false 表示等待中（下一帧再调用）。
        /// </summary>
        internal static bool ExecuteSceneStep(VerifyJob job)
        {
            // 场景验证需要在 Edit Mode 中进行，等待 Play Mode 完全退出
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (EditorApplication.isPlaying)
                    EditorApplication.isPlaying = false;
                return false; // 等下一帧 Play Mode 完全退出后再继续
            }

            if (!job.ValidateScene)
            {
                job.Steps["scene"] = StepResult(
                    ("skipped", true),
                    ("reason", "validateScene is false"));
                return true;
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
                    return true;
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

            return true;
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

            // 初始化 UI 阶段：测试运行后场景可能已被重置，需要先恢复场景
            if (job.CurrentUIPhase == UIPhase.NotStarted)
            {
                job.StepStartTime = DateTime.Now;

                // 如果已在 Play Mode（或正在进入），直接等待初始化
                // 使用 isPlayingOrWillChangePlaymode 避免域重载过渡期误判
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    job.CurrentUIPhase = UIPhase.WaitingInit;
                    job.UIWaitStart = DateTime.Now;
                    return false;
                }

                // 测试框架运行后会重置场景，需要重新加载目标场景
                // 如果指定了 uiScenePath，先加载该场景再进入 Play Mode
                if (!string.IsNullOrEmpty(job.UIScenePath))
                {
                    try
                    {
                        SkillsLogger.Log($"[Verify] UI 检查前重新加载场景: {job.UIScenePath}");
                        EditorSceneManager.OpenScene(job.UIScenePath, OpenSceneMode.Single);
                        job.CurrentUIPhase = UIPhase.LoadingScene;
                    }
                    catch (Exception ex)
                    {
                        job.Steps["ui"] = StepResult(
                            ("skipped", true),
                            ("reason", $"Failed to load scene '{job.UIScenePath}': {ex.Message}"));
                        job.CurrentUIPhase = UIPhase.Done;
                        return true;
                    }
                    return false;
                }

                // 没有指定场景路径，直接进入 Play Mode
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
                // 场景加载完成后进入 Play Mode
                case UIPhase.LoadingScene:
                    SkillsLogger.Log($"[Verify] 场景已加载，准备进入 Play Mode");
                    job.CurrentUIPhase = UIPhase.EnteringPlay;
                    try
                    {
                        EditorApplication.isPlaying = true;
                    }
                    catch (Exception ex)
                    {
                        job.Steps["ui"] = StepResult(
                            ("skipped", true),
                            ("reason", $"Failed to enter Play Mode after scene load: {ex.Message}"));
                        job.CurrentUIPhase = UIPhase.Done;
                        return true;
                    }
                    return false;

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
                        // 如果指定了点击选择器，先点击再等待；否则直接查询
                        if (!string.IsNullOrEmpty(job.UICheckClickSelector))
                            job.CurrentUIPhase = UIPhase.ClickingElement;
                        else
                            job.CurrentUIPhase = UIPhase.Querying;
                    }
                    return false;

                // 点击指定元素（如"开始"按钮）
                case UIPhase.ClickingElement:
                    try
                    {
                        SkillsLogger.Log($"[Verify] UI 检查：点击元素 '{job.UICheckClickSelector}'");
                        var clickResult = InteractionSkills.UIClick(job.UICheckClickSelector);

                        // 检查点击是否成功
                        var errorProp = clickResult.GetType().GetProperty("error");
                        if (errorProp?.GetValue(clickResult) != null)
                        {
                            var errorMsg = errorProp.GetValue(clickResult).ToString();
                            SkillsLogger.LogWarning($"[Verify] 点击元素失败: {errorMsg}");
                            job.Steps["ui"] = StepResult(
                                ("passed", false),
                                ("error", $"Failed to click '{job.UICheckClickSelector}': {errorMsg}"));
                            job.Failed = true;
                            EditorApplication.isPlaying = false;
                            job.CurrentUIPhase = UIPhase.ExitingPlay;
                            return false;
                        }

                        SkillsLogger.Log($"[Verify] 成功点击元素 '{job.UICheckClickSelector}'");

                        // 如果指定了等待选择器，进入等待阶段
                        if (!string.IsNullOrEmpty(job.UICheckWaitSelector))
                        {
                            job.CurrentUIPhase = UIPhase.WaitingForElement;
                            job.UIWaitStart = DateTime.Now;
                        }
                        else
                        {
                            job.CurrentUIPhase = UIPhase.Querying;
                        }
                    }
                    catch (Exception ex)
                    {
                        SkillsLogger.LogError($"[Verify] 点击元素异常: {ex.Message}");
                        job.Steps["ui"] = StepResult(
                            ("passed", false),
                            ("error", $"Click exception: {ex.Message}"));
                        job.Failed = true;
                        EditorApplication.isPlaying = false;
                        job.CurrentUIPhase = UIPhase.ExitingPlay;
                    }
                    return false;

                // 等待指定元素出现（如 HUD 的 .goal-progress）
                case UIPhase.WaitingForElement:
                {
                    var waitElapsed = (DateTime.Now - job.UIWaitStart).TotalSeconds;
                    var waitTimeout = 15; // 最多等待 15 秒

                    if (waitElapsed > waitTimeout)
                    {
                        SkillsLogger.LogWarning($"[Verify] 等待元素 '{job.UICheckWaitSelector}' 超时 ({waitTimeout}s)");
                        job.Steps["ui"] = StepResult(
                            ("passed", false),
                            ("error", $"Timeout waiting for '{job.UICheckWaitSelector}' after {waitTimeout}s"));
                        job.Failed = true;
                        EditorApplication.isPlaying = false;
                        job.CurrentUIPhase = UIPhase.ExitingPlay;
                        return false;
                    }

                    // 直接用 InteractionSkills 内部的 FindElementAcrossDocuments 逻辑查找元素
                    // 但 FindElementAcrossDocuments 是 private，所以用 WaitForElement 的同步检查方式
                    // 通过 UIClick 的方式探测元素是否存在（不实际点击，只查找）
                    // 更简单的方法：用 UIToolkitSkills.Tree 查找，或直接调用 WaitForElement
                    try
                    {
                        // 使用 WaitForElement 启动异步等待（仅在首次进入时启动）
                        if (string.IsNullOrEmpty(job.UIWaitJobId))
                        {
                            var waitResult = InteractionSkills.WaitForElement(
                                job.UICheckWaitSelector,
                                condition: "exists",
                                timeout: waitTimeout);

                            // 检查是否立即满足
                            var statusProp = waitResult.GetType().GetProperty("status");
                            var status = statusProp?.GetValue(waitResult)?.ToString();

                            if (status == "found")
                            {
                                SkillsLogger.Log($"[Verify] 元素 '{job.UICheckWaitSelector}' 已存在，继续查询");
                                job.CurrentUIPhase = UIPhase.Querying;
                                return false;
                            }

                            // 获取 jobId 用于后续轮询
                            var jobIdProp = waitResult.GetType().GetProperty("jobId");
                            job.UIWaitJobId = jobIdProp?.GetValue(waitResult)?.ToString() ?? "";

                            if (string.IsNullOrEmpty(job.UIWaitJobId))
                            {
                                // 没有 jobId 且没有 found，说明出错了
                                var errorProp = waitResult.GetType().GetProperty("error");
                                var errorMsg = errorProp?.GetValue(waitResult)?.ToString() ?? "Unknown error";
                                job.Steps["ui"] = StepResult(
                                    ("passed", false),
                                    ("error", $"WaitForElement failed: {errorMsg}"));
                                job.Failed = true;
                                EditorApplication.isPlaying = false;
                                job.CurrentUIPhase = UIPhase.ExitingPlay;
                            }
                            return false;
                        }

                        // 轮询等待结果
                        var pollResult = InteractionSkills.WaitGetResult(job.UIWaitJobId);
                        var pollStatusProp = pollResult.GetType().GetProperty("status");
                        var pollStatus = pollStatusProp?.GetValue(pollResult)?.ToString();

                        if (pollStatus == "found")
                        {
                            SkillsLogger.Log($"[Verify] 元素 '{job.UICheckWaitSelector}' 已出现，继续查询");
                            job.CurrentUIPhase = UIPhase.Querying;
                        }
                        else if (pollStatus == "timeout" || pollStatus == "error")
                        {
                            var msgProp = pollResult.GetType().GetProperty("message");
                            var msg = msgProp?.GetValue(pollResult)?.ToString() ?? pollStatus;
                            SkillsLogger.LogWarning($"[Verify] 等待元素失败: {msg}");
                            job.Steps["ui"] = StepResult(
                                ("passed", false),
                                ("error", $"WaitForElement {pollStatus}: {msg}"));
                            job.Failed = true;
                            EditorApplication.isPlaying = false;
                            job.CurrentUIPhase = UIPhase.ExitingPlay;
                        }
                        // else still waiting
                    }
                    catch (Exception ex)
                    {
                        SkillsLogger.LogError($"[Verify] 等待元素异常: {ex.Message}");
                        job.Steps["ui"] = StepResult(
                            ("passed", false),
                            ("error", $"WaitForElement exception: {ex.Message}"));
                        job.Failed = true;
                        EditorApplication.isPlaying = false;
                        job.CurrentUIPhase = UIPhase.ExitingPlay;
                    }
                    return false;
                }

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
