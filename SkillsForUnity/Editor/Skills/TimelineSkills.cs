using UnityEngine;
using UnityEditor;
using UnityEngine.Timeline;
using UnityEngine.Playables;
using System.Linq;

namespace UnitySkills
{
    /// <summary>
    /// Timeline skills - Create assets, tracks, clips.
    /// </summary>
    public static class TimelineSkills
    {
        [UnitySkill("timeline_create", "Create a new Timeline asset and Director instance")]
        public static object TimelineCreate(string name, string folder = "Assets/Timelines")
        {
             if (!System.IO.Directory.Exists(folder))
                System.IO.Directory.CreateDirectory(folder);

            string assetPath = System.IO.Path.Combine(folder, name + ".playable");
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            // Create Asset
            var timelineAsset = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timelineAsset, assetPath);
            
            // Create GameObject
            var go = new GameObject(name);
            var director = go.AddComponent<PlayableDirector>();
            director.playableAsset = timelineAsset;

            AssetDatabase.SaveAssets();
            
            return new 
            { 
                success = true, 
                assetPath, 
                gameObjectName = go.name, 
                directorInstanceId = director.GetInstanceID() 
            };
        }

        [UnitySkill("timeline_add_audio_track", "Add an Audio track to a Timeline")]
        public static object TimelineAddAudioTrack(string directorObjectName, string trackName = "Audio Track")
        {
            var (go, findErr) = GameObjectFinder.FindOrError(name: directorObjectName);
            if (findErr != null) return findErr;

            var director = go.GetComponent<PlayableDirector>();
            if (director == null) return new { error = "PlayableDirector component not found" };

            var timeline = director.playableAsset as TimelineAsset;
            if (timeline == null) return new { error = "No TimelineAsset assigned to Director" };

            var track = timeline.CreateTrack<AudioTrack>(null, trackName);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return new { success = true, trackName = track.name };
        }

        [UnitySkill("timeline_add_animation_track", "Add an Animation track to a Timeline, optionally binding an object")]
        public static object TimelineAddAnimationTrack(string directorObjectName, string trackName = "Animation Track", string bindingObjectName = null)
        {
            var (go, findErr) = GameObjectFinder.FindOrError(name: directorObjectName);
            if (findErr != null) return findErr;

            var director = go.GetComponent<PlayableDirector>();
            if (director == null) return new { error = "PlayableDirector component not found" };

            var timeline = director.playableAsset as TimelineAsset;
            if (timeline == null) return new { error = "No TimelineAsset assigned to Director" };

            var track = timeline.CreateTrack<AnimationTrack>(null, trackName);

            if (!string.IsNullOrEmpty(bindingObjectName))
            {
                var (bindingGo, bindErr) = GameObjectFinder.FindOrError(name: bindingObjectName);
                if (bindErr != null) return bindErr;

                var animator = bindingGo.GetComponent<Animator>();
                if (animator == null) animator = bindingGo.AddComponent<Animator>();

                director.SetGenericBinding(track, animator);
            }
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return new { success = true, trackName = track.name, boundObject = bindingObjectName ?? "None" };
        }

        [UnitySkill("timeline_add_activation_track", "Add an Activation track to control object visibility")]
        public static object TimelineAddActivationTrack(string directorObjectName, string trackName = "Activation Track")
        {
            var (timeline, director, err) = GetTimeline(directorObjectName);
            if (err != null) return err;
            var track = timeline.CreateTrack<ActivationTrack>(null, trackName);
            AssetDatabase.SaveAssets();
            return new { success = true, trackName = track.name };
        }

        [UnitySkill("timeline_add_control_track", "Add a Control track for nested Timelines or prefab spawning")]
        public static object TimelineAddControlTrack(string directorObjectName, string trackName = "Control Track")
        {
            var (timeline, director, err) = GetTimeline(directorObjectName);
            if (err != null) return err;
            var track = timeline.CreateTrack<ControlTrack>(null, trackName);
            AssetDatabase.SaveAssets();
            return new { success = true, trackName = track.name };
        }

        [UnitySkill("timeline_add_signal_track", "Add a Signal track for event markers")]
        public static object TimelineAddSignalTrack(string directorObjectName, string trackName = "Signal Track")
        {
            var (timeline, director, err) = GetTimeline(directorObjectName);
            if (err != null) return err;
            var track = timeline.CreateTrack<SignalTrack>(null, trackName);
            AssetDatabase.SaveAssets();
            return new { success = true, trackName = track.name };
        }

        [UnitySkill("timeline_remove_track", "Remove a track by name from a Timeline")]
        public static object TimelineRemoveTrack(string directorObjectName, string trackName)
        {
            var (timeline, director, err) = GetTimeline(directorObjectName);
            if (err != null) return err;
            var track = timeline.GetOutputTracks().FirstOrDefault(t => t.name == trackName);
            if (track == null) return new { error = $"Track not found: {trackName}" };
            timeline.DeleteTrack(track);
            AssetDatabase.SaveAssets();
            return new { success = true, removed = trackName };
        }

        [UnitySkill("timeline_list_tracks", "List all tracks in a Timeline")]
        public static object TimelineListTracks(string directorObjectName)
        {
            var (timeline, director, err) = GetTimeline(directorObjectName);
            if (err != null) return err;
            var tracks = timeline.GetOutputTracks().Select(t => new
            {
                name = t.name, type = t.GetType().Name,
                muted = t.muted, clipCount = t.GetClips().Count()
            }).ToArray();
            return new { count = tracks.Length, tracks };
        }

        [UnitySkill("timeline_add_clip", "Add a clip to a track by track name")]
        public static object TimelineAddClip(string directorObjectName, string trackName, double start = 0, double duration = 1)
        {
            var (timeline, director, err) = GetTimeline(directorObjectName);
            if (err != null) return err;
            var track = timeline.GetOutputTracks().FirstOrDefault(t => t.name == trackName);
            if (track == null) return new { error = $"Track not found: {trackName}" };
            var clip = track.CreateDefaultClip();
            clip.start = start;
            clip.duration = duration;
            AssetDatabase.SaveAssets();
            return new { success = true, trackName, clipStart = clip.start, clipDuration = clip.duration };
        }

        [UnitySkill("timeline_set_duration", "Set Timeline duration and wrap mode")]
        public static object TimelineSetDuration(string directorObjectName, double duration, string wrapMode = null)
        {
            var (timeline, director, err) = GetTimeline(directorObjectName);
            if (err != null) return err;
            timeline.fixedDuration = duration;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            if (!string.IsNullOrEmpty(wrapMode))
            {
                if (System.Enum.TryParse<DirectorWrapMode>(wrapMode, true, out var wm))
                    director.extrapolationMode = wm;
            }
            AssetDatabase.SaveAssets();
            return new { success = true, duration, wrapMode = director.extrapolationMode.ToString() };
        }

        [UnitySkill("timeline_play", "Play, pause, or stop a Timeline (Editor preview)")]
        public static object TimelinePlay(string directorObjectName, string action = "play")
        {
            var (go, findErr) = GameObjectFinder.FindOrError(name: directorObjectName);
            if (findErr != null) return findErr;
            var director = go.GetComponent<PlayableDirector>();
            if (director == null) return new { error = "PlayableDirector not found" };
            switch (action.ToLower())
            {
                case "play": director.Play(); break;
                case "pause": director.Pause(); break;
                case "stop": director.Stop(); break;
                default: return new { error = $"Unknown action: {action}. Use play/pause/stop" };
            }
            return new { success = true, action, time = director.time };
        }

        [UnitySkill("timeline_set_binding", "Set the binding object for a track")]
        public static object TimelineSetBinding(string directorObjectName, string trackName, string bindingObjectName)
        {
            var (timeline, director, err) = GetTimeline(directorObjectName);
            if (err != null) return err;
            var track = timeline.GetOutputTracks().FirstOrDefault(t => t.name == trackName);
            if (track == null) return new { error = $"Track not found: {trackName}" };
            var (bindGo, bindErr) = GameObjectFinder.FindOrError(name: bindingObjectName);
            if (bindErr != null) return bindErr;
            director.SetGenericBinding(track, bindGo);
            return new { success = true, trackName, boundTo = bindingObjectName };
        }

        private static (TimelineAsset, PlayableDirector, object) GetTimeline(string directorObjectName)
        {
            var (go, findErr) = GameObjectFinder.FindOrError(name: directorObjectName);
            if (findErr != null) return (null, null, findErr);
            var director = go.GetComponent<PlayableDirector>();
            if (director == null) return (null, null, new { error = "PlayableDirector not found" });
            var timeline = director.playableAsset as TimelineAsset;
            if (timeline == null) return (null, null, new { error = "No TimelineAsset assigned" });
            return (timeline, director, null);
        }
    }
}
