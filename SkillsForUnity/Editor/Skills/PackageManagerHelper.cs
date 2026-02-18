using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System;
using System.Collections.Generic;
using System.Linq;
using PkgInfo = UnityEditor.PackageManager.PackageInfo;

namespace UnitySkills
{
    /// <summary>
    /// Unity Package Manager API wrapper.
    /// </summary>
    public static class PackageManagerHelper
    {
        public const string CinemachinePackageId = "com.unity.cinemachine";
        public const string SplinesPackageId = "com.unity.splines";
        public const string Cinemachine2Version = "2.10.5";
        public const string Cinemachine3Version = "3.1.3";
        public const string SplinesVersion = "2.8.0";
        public const string SplinesVersionUnity6 = "2.8.3";

        private static ListRequest _listRequest;
        private static AddRequest _addRequest;
        private static RemoveRequest _removeRequest;
        private static Dictionary<string, PkgInfo> _installedPackages;
        private static bool _isRefreshing;
        private static Action<bool, string> _pendingCallback;
        // Store the current List request callback for correct unsubscription in EditorApplication.update
        private static Action<bool> _listCallback;

        public static bool IsRefreshing => _isRefreshing;
        public static Dictionary<string, PkgInfo> InstalledPackages => _installedPackages;

        /// <summary>
        /// Refresh the installed packages list.
        /// </summary>
        public static void RefreshPackageList(Action<bool> callback = null)
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            _listCallback = callback;
            _listRequest = Client.List(true);
            // Use a named method instead of lambda to ensure -= can correctly match and unsubscribe
            EditorApplication.update += OnListProgress;
        }

        private static void OnListProgress()
        {
            if (_listRequest == null || !_listRequest.IsCompleted) return;
            EditorApplication.update -= OnListProgress;

            _isRefreshing = false;
            var callback = _listCallback;
            _listCallback = null;

            if (_listRequest.Status == StatusCode.Success)
            {
                _installedPackages = new Dictionary<string, PkgInfo>();
                foreach (var pkg in _listRequest.Result)
                    _installedPackages[pkg.name] = pkg;
                callback?.Invoke(true);
            }
            else
            {
                Debug.LogError($"[PackageManager] List failed: {_listRequest.Error?.message}");
                callback?.Invoke(false);
            }
        }

        /// <summary>
        /// Check if a package is installed.
        /// </summary>
        public static bool IsPackageInstalled(string packageId)
        {
            return _installedPackages != null && _installedPackages.ContainsKey(packageId);
        }

        /// <summary>
        /// Get the installed version of a package.
        /// </summary>
        public static string GetInstalledVersion(string packageId)
        {
            if (_installedPackages != null && _installedPackages.TryGetValue(packageId, out var info))
                return info.version;
            return null;
        }

        /// <summary>
        /// Install a package (async).
        /// </summary>
        public static void InstallPackage(string packageId, string version, Action<bool, string> callback)
        {
            if (_addRequest != null && !_addRequest.IsCompleted)
            {
                callback?.Invoke(false, "Another install operation is in progress");
                return;
            }

            var identifier = string.IsNullOrEmpty(version) ? packageId : $"{packageId}@{version}";
            _addRequest = Client.Add(identifier);
            _pendingCallback = callback;
            EditorApplication.update += OnAddProgress;
        }

        private static void OnAddProgress()
        {
            if (!_addRequest.IsCompleted) return;
            EditorApplication.update -= OnAddProgress;

            var cb = _pendingCallback;
            _pendingCallback = null;

            if (_addRequest.Status == StatusCode.Success)
            {
                RefreshPackageList();
                cb?.Invoke(true, _addRequest.Result.version);
            }
            else
            {
                cb?.Invoke(false, _addRequest.Error?.message ?? "Unknown error");
            }
        }

        /// <summary>
        /// Remove a package (async).
        /// </summary>
        public static void RemovePackage(string packageId, Action<bool, string> callback)
        {
            if (_removeRequest != null && !_removeRequest.IsCompleted)
            {
                callback?.Invoke(false, "Another remove operation is in progress");
                return;
            }

            _removeRequest = Client.Remove(packageId);
            _pendingCallback = callback;
            EditorApplication.update += OnRemoveProgress;
        }

        private static void OnRemoveProgress()
        {
            if (!_removeRequest.IsCompleted) return;
            EditorApplication.update -= OnRemoveProgress;

            var cb = _pendingCallback;
            _pendingCallback = null;

            if (_removeRequest.Status == StatusCode.Success)
            {
                RefreshPackageList();
                cb?.Invoke(true, null);
            }
            else
            {
                cb?.Invoke(false, _removeRequest.Error?.message ?? "Unknown error");
            }
        }

        /// <summary>
        /// 获取当前 Unity 版本推荐的 Splines 版本
        /// </summary>
        public static string GetRecommendedSplinesVersion()
        {
#if UNITY_6000_0_OR_NEWER
            return SplinesVersionUnity6;
#else
            return SplinesVersion;
#endif
        }

        /// <summary>
        /// 安装 Splines 包
        /// </summary>
        public static void InstallSplines(Action<bool, string> callback)
        {
            InstallPackage(SplinesPackageId, GetRecommendedSplinesVersion(), callback);
        }

        /// <summary>
        /// 安装 Cinemachine（自动处理依赖）
        /// </summary>
        public static void InstallCinemachine(bool useVersion3, Action<bool, string> callback)
        {
            if (useVersion3)
            {
                // CM3 requires Splines to be installed first
                if (!IsPackageInstalled(SplinesPackageId))
                {
                    InstallPackage(SplinesPackageId, GetRecommendedSplinesVersion(), (success, msg) =>
                    {
                        if (success)
                            InstallPackage(CinemachinePackageId, Cinemachine3Version, callback);
                        else
                            callback?.Invoke(false, $"Failed to install Splines dependency: {msg}");
                    });
                }
                else
                {
                    InstallPackage(CinemachinePackageId, Cinemachine3Version, callback);
                }
            }
            else
            {
                InstallPackage(CinemachinePackageId, Cinemachine2Version, callback);
            }
        }

        /// <summary>
        /// Get Cinemachine installation status.
        /// </summary>
        public static (bool installed, string version, bool isVersion3) GetCinemachineStatus()
        {
            if (!IsPackageInstalled(CinemachinePackageId))
                return (false, null, false);

            var version = GetInstalledVersion(CinemachinePackageId);
            var isV3 = version != null && version.StartsWith("3.");
            return (true, version, isV3);
        }

        /// <summary>
        /// Initialize (refresh package list on first load and auto-install Cinemachine).
        /// </summary>
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            // Delay execution to wait for Package Manager initialization to complete
            EditorApplication.delayCall += () =>
            {
                RefreshPackageList(success =>
                {
                    if (success) AutoInstallCinemachineIfNeeded();
                });
            };
        }

        private static int _autoInstallRetryCount = 0;
        private static bool _autoInstallInProgress = false;
        private static double _nextRetryTime = 0;
        private const int MaxAutoInstallRetries = 5;
        private const double RetryDelaySeconds = 3.0;

        /// <summary>
        /// Auto-install Cinemachine if not already installed.
        /// Unity 6+ defaults to CM3, Unity 2022 and below defaults to CM2.
        /// </summary>
        private static void AutoInstallCinemachineIfNeeded()
        {
            if (_autoInstallInProgress || IsPackageInstalled(CinemachinePackageId)) return;
            _autoInstallInProgress = true;

#if UNITY_6000_0_OR_NEWER
            bool useV3 = true;
#else
            bool useV3 = false;
#endif
            Debug.Log($"[UnitySkills] Auto-installing Cinemachine {(useV3 ? "3.x" : "2.x")}...");
            InstallCinemachine(useV3, (success, msg) =>
            {
                if (success)
                {
                    Debug.Log($"[UnitySkills] Cinemachine {msg} installed successfully!");
                    _autoInstallRetryCount = 0;
                    _autoInstallInProgress = false;
                }
                else if (msg != null && msg.Contains("in progress") && _autoInstallRetryCount < MaxAutoInstallRetries)
                {
                    _autoInstallRetryCount++;
                    Debug.Log($"[UnitySkills] Package Manager busy, retrying in {RetryDelaySeconds}s... ({_autoInstallRetryCount}/{MaxAutoInstallRetries})");
                    _nextRetryTime = EditorApplication.timeSinceStartup + RetryDelaySeconds;
                    _autoInstallInProgress = false;
                    EditorApplication.update += WaitAndRetryAutoInstall;
                }
                else
                {
                    Debug.LogWarning($"[UnitySkills] Failed to auto-install Cinemachine: {msg}");
                    _autoInstallRetryCount = 0;
                    _autoInstallInProgress = false;
                }
            });
        }

        private static void WaitAndRetryAutoInstall()
        {
            if (EditorApplication.timeSinceStartup < _nextRetryTime) return;
            EditorApplication.update -= WaitAndRetryAutoInstall;
            AutoInstallCinemachineIfNeeded();
        }
    }
}
