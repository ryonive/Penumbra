using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui.Services;
using Penumbra.Collections;
using Penumbra.CrashHandler;
using Penumbra.GameData;
using Penumbra.Interop.PathResolving;
using Penumbra.Services;

namespace Penumbra.Interop.Hooks.Animation;

/// <summary>
/// The timeline object loads the requested .tmb and .pap files. The .tmb files load the respective .avfx files.
/// We can obtain the associated game object from the timelines 28'th vfunc and use that to apply the correct collection.
/// </summary>
public sealed unsafe class LoadTimelineResources : FastHook<LoadTimelineResources.Delegate>
{
    private readonly GameState           _state;
    private readonly CollectionResolver  _collectionResolver;
    private readonly ICondition          _conditions;
    private readonly IObjectTable        _objects;
    private readonly CrashHandlerService _crashHandler;

    public LoadTimelineResources(HookManager hooks, GameState state, CollectionResolver collectionResolver, ICondition conditions,
        IObjectTable objects, CrashHandlerService crashHandler)
    {
        _state              = state;
        _collectionResolver = collectionResolver;
        _conditions         = conditions;
        _objects            = objects;
        _crashHandler       = crashHandler;
        Task                = hooks.CreateHook<Delegate>("Load Timeline Resources", Sigs.LoadTimelineResources, Detour, true);
    }

    public delegate ulong Delegate(nint timeline);

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private ulong Detour(nint timeline)
    {
        Penumbra.Log.Excessive($"[Load Timeline Resources] Invoked on {timeline:X}.");
        // Do not check timeline loading in cutscenes.
        if (_conditions[ConditionFlag.OccupiedInCutSceneEvent] || _conditions[ConditionFlag.WatchingCutscene78])
            return Task.Result.Original(timeline);

        var newData = GetDataFromTimeline(_objects, _collectionResolver, timeline);
        var last    = _state.SetAnimationData(newData);

#if false
        // This is called far too often and spams the log too much.
        _crashHandler.LogAnimation(newData.AssociatedGameObject, newData.ModCollection, AnimationInvocationType.LoadTimelineResources);
#endif
        var ret  = Task.Result.Original(timeline);
        _state.RestoreAnimationData(last);
        return ret;
    }

    /// <summary> Use timelines vfuncs to obtain the associated game object. </summary>
    public static ResolveData GetDataFromTimeline(IObjectTable objects, CollectionResolver resolver, nint timeline)
    {
        try
        {
            if (timeline != nint.Zero)
            {
                var getGameObjectIdx = ((delegate* unmanaged<nint, int>**)timeline)[0][Offsets.GetGameObjectIdxVfunc];
                var idx              = getGameObjectIdx(timeline);
                if (idx >= 0 && idx < objects.Length)
                {
                    var obj = (GameObject*)objects.GetObjectAddress(idx);
                    return obj != null ? resolver.IdentifyCollection(obj, true) : ResolveData.Invalid;
                }
            }
        }
        catch (Exception e)
        {
            Penumbra.Log.Error($"Error getting timeline data for 0x{timeline:X}:\n{e}");
        }

        return ResolveData.Invalid;
    }
}
