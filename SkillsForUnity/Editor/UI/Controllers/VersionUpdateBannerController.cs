using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnitySkills
{
    /// <summary>
    /// Presents the cached/latest stable release as a compact global notice.
    /// Network and cache ownership stay in <see cref="VersionCheckService"/>.
    /// </summary>
    internal sealed class VersionUpdateBannerController
    {
        private readonly VisualElement _banner;
        private readonly Label _message;
        private readonly Button _updateNowButton;
        private readonly Button _viewReleaseButton;
        private readonly Button _dismissButton;

        private const double CheckPollIntervalSeconds = 60.0;

        private string _lastSnapshot;
        private double _nextCheckAtEditorTime;
        private VersionCheckService.ReleaseInfo _displayedRelease;
        private bool _updating;
        // Manifest reads are cheap but the install source cannot change mid-session, so detect lazily once.
        private PackageManagerHelper.SelfInstallKind? _installKind;

        public VersionUpdateBannerController(VisualElement root)
        {
            _banner = root.Q<VisualElement>("version-update-banner");
            _message = root.Q<Label>("version-update-message");
            _updateNowButton = root.Q<Button>("version-update-now-btn");
            _viewReleaseButton = root.Q<Button>("version-update-view-btn");
            _dismissButton = root.Q<Button>("version-update-dismiss-btn");

            if (_updateNowButton != null)
                _updateNowButton.clicked += StartDirectUpdate;
            if (_viewReleaseButton != null)
                _viewReleaseButton.clicked += OpenRelease;
            if (_dismissButton != null)
                _dismissButton.clicked += Dismiss;

            VersionCheckService.StartCheck();
            _nextCheckAtEditorTime = EditorApplication.timeSinceStartup + CheckPollIntervalSeconds;
            RefreshLocalization();
        }

        public void UpdateLiveData()
        {
            PollForReleaseCheck();

            var release = VersionCheckService.LatestRelease;
            var shouldShow = VersionCheckService.HasUpdate;
            var snapshot = shouldShow
                ? $"{SkillsLogger.Version}|{release?.TagName}|{release?.ReleaseUrl}|show"
                : $"{SkillsLogger.Version}|{release?.Version}|hide";

            if (snapshot == _lastSnapshot) return;
            _lastSnapshot = snapshot;

            if (!shouldShow || release == null)
            {
                _displayedRelease = null;
                _banner?.EnableInClassList("is-hidden", true);
                return;
            }

            _displayedRelease = release;
            RefreshMessage(release);
            // Local/embedded installs cannot self-update; hide the one-click button there.
            UiVisibility.SetVisible(_updateNowButton,
                !_updating && InstallKind != PackageManagerHelper.SelfInstallKind.Unsupported);
            _banner?.EnableInClassList("is-hidden", false);
        }

        private PackageManagerHelper.SelfInstallKind InstallKind =>
            _installKind ??= PackageManagerHelper.DetectSelfInstallKind();

        public void RefreshLocalization()
        {
            if (_updateNowButton != null)
                _updateNowButton.text = SkillsLocalization.Get("version_update_now");
            if (_viewReleaseButton != null)
                _viewReleaseButton.text = SkillsLocalization.Get("version_update_view_release");
            if (_dismissButton != null)
                _dismissButton.tooltip = SkillsLocalization.Get("version_update_dismiss_tip");

            _lastSnapshot = null;
            UpdateLiveData();
        }

        private void PollForReleaseCheck()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < _nextCheckAtEditorTime) return;

            _nextCheckAtEditorTime = now + CheckPollIntervalSeconds;
            VersionCheckService.StartCheck();
        }

        private void RefreshMessage(VersionCheckService.ReleaseInfo release)
        {
            if (_message == null) return;

            _message.text = string.Format(
                SkillsLocalization.Get("version_update_message_fmt"),
                SkillsLogger.Version,
                release.Version);
        }

        private void OpenRelease()
        {
            var url = _displayedRelease?.ReleaseUrl;
            if (!string.IsNullOrWhiteSpace(url)) Application.OpenURL(url);
        }

        /// <summary>
        /// One-click self-update straight from the banner. The banner only ever advertises a
        /// stable release, so the update always targets "#v{release.Version}".
        /// </summary>
        private void StartDirectUpdate()
        {
            var release = _displayedRelease;
            if (release == null || _updating) return;

            if (InstallKind == PackageManagerHelper.SelfInstallKind.Unsupported)
            {
                OpenRelease();
                return;
            }

            _updating = true;
            _updateNowButton?.SetEnabled(false);
            _viewReleaseButton?.SetEnabled(false);
            if (_message != null)
                _message.text = SkillsLocalization.Get("update_check_updating");

            PackageManagerHelper.UpdateSelf(
                PackageManagerHelper.SelfInstallKind.Stable, release.Version, (success, error) =>
                {
                    if (success)
                    {
                        // The package swap triggers a domain reload that tears this banner down.
                        if (_message != null)
                            _message.text = SkillsLocalization.Get("update_check_done");
                        return;
                    }

                    _updating = false;
                    _updateNowButton?.SetEnabled(true);
                    _viewReleaseButton?.SetEnabled(true);
                    if (_message != null)
                    {
                        _message.text = SkillsLocalization.Get("update_check_failed_fmt",
                            string.IsNullOrEmpty(error)
                                ? SkillsLocalization.Get("update_check_reason_unknown")
                                : error);
                    }
                });
        }

        private void Dismiss()
        {
            var release = _displayedRelease;
            if (release == null) return;

            VersionCheckService.Dismiss(release);
            _displayedRelease = null;
            _lastSnapshot = null;
            UpdateLiveData();
        }
    }
}

// Producer:Betsy
