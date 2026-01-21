# Changelog

All notable changes to UnitySkills will be documented in this file.

## [1.3.3] - 2025-01-24

### Added - Domain Reload Auto-Recovery

**Problem Solved**: Previously, when a script file was created or modified via `script_create` skill, Unity would recompile all scripts which triggers a Domain Reload. This destroyed the running server instance, requiring manual restart.

**Solution**: Implemented comprehensive Domain Reload recovery system:

- **AssemblyReloadEvents Integration**: Server now listens to `beforeAssemblyReload` and `afterAssemblyReload` events
- **State Persistence**: Server running state is saved to `EditorPrefs` before Domain Reload
- **Auto-Restart**: Server automatically restarts after compilation completes using `EditorApplication.delayCall`
- **User Control**: New "Auto-restart after compile" toggle in the Server tab to enable/disable this behavior

### Added - Live Server Statistics

- Real-time queue count display with color coding (gray=0, cyan=pending, yellow=backlog)
- Total processed requests counter
- Architecture display showing "Producer-Consumer" pattern

### Changed - Server Architecture Improvements

- **Strict Producer-Consumer Pattern**: HTTP thread now ONLY receives and enqueues requests
- **Main Thread Processing**: ALL Unity API calls and business logic run on main thread
- **Thread-Safe Timing**: Replaced `EditorApplication.timeSinceStartup` with `DateTime.UtcNow.Ticks` to avoid cross-thread Unity API access
- **Improved Shutdown**: `Stop(bool permanent)` and `StopPermanent()` methods for controlled shutdown behavior

### Changed - UI Improvements

- New localization strings for auto-restart feature (English/Chinese)
- Stop button now uses `StopPermanent()` for explicit user-initiated stops

### Technical Details

**New API Methods**:
- `SkillsHttpServer.AutoStart` - Gets/sets auto-start preference (persisted in EditorPrefs)
- `SkillsHttpServer.StopPermanent()` - Stops server without auto-restart
- `SkillsHttpServer.Stop(bool permanent)` - Stops server with optional permanent flag

**Health Endpoint Updates**:
The `/health` endpoint now returns additional fields:
```json
{
  "status": "ok",
  "version": "1.3.3",
  "autoRestart": true,
  "domainReloadRecovery": true,
  ...
}
```

## [1.3.2] - 2025-01-XX

- Initial Producer-Consumer architecture
- Server stability improvements
- Live statistics display

## [1.3.0] - 2025-01-XX

- 76+ Skills available
- Chinese/English UI support
- One-click AI tool configuration
- REST API server on port 8090
