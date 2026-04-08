using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized gizmo/debug-drawing call aggregator for Unity.
/// </summary>
/// 
/// <remarks>
/// Unity requires gizmo drawing to occur inside <see cref="MonoBehaviour.OnDrawGizmos"/> (or related gizmo callbacks).
/// In larger projects, that can lead to many scattered components each implementing their own gizmo methods.

/// This utility class provides a simple way for arbitrary systems to enqueue gizmo draw calls
/// and have them all executed from a single OnDrawGizmos entry point.
///
/// How it’s meant to be used:
/// - Any code can request a named batcher via <see cref="GetInstance(string)"/>.
/// - That code can then enqueue gizmo drawing work with <see cref="BatchCall(Action)"/>.
/// - When Unity calls <see cref="OnDrawGizmos"/>, the “root” instance draws *all* enqueued calls across all batchers.
///
/// Important behavior:
/// - The first-created instance becomes the global "root". Only the root actually executes drawing.
/// - Additional instances exist only to hold separate queues (channels) of calls.
/// - Calls remain enqueued until cleared via <see cref="ClearAllBatchedCalls"/> (or until domain reload / quit).
///
/// Lifetime and scene impact
/// - Instances are created as new <see cref="GameObject"/>s named "DebugDrawingBatcher_{pName}".
/// - They are not parented, hidden, or marked as DontDestroyOnLoad in this version.
///   This means they will exist in the active scene and may accumulate if not managed carefully.
/// - The static collections are cleared in <see cref="OnApplicationQuit"/>, but note that Unity Editor domain reload
///   / assembly reload behavior can differ from player quit behavior.
///
/// Threading:
/// This class is not thread-safe. Enqueue calls from the Unity main thread only.
///
/// Performance:
/// The work executed is whatever you enqueue. If you enqueue many calls or expensive logic, editor scene view drawing may become slow.
/// </remarks>
public class DebugDrawingBatcher : MonoBehaviour
{
    // Maps a batcher name/channel to its instance, e.g "rooms", "doors", etc
    private static Dictionary<string, DebugDrawingBatcher> instances = new();

    /// The first-created batcher instance. Only this instance performs drawing in <see cref="OnDrawGizmos"/>.
    /// This ensures that drawing happens exactly once per frame (per gizmo pass), 
    /// while still allowing multiple named queues to contribute actions.
    private static DebugDrawingBatcher root = null;

    /// All batcher instances created so far (including the root and any named channels).
    private static List<DebugDrawingBatcher> debugDrawingBatchers = new List<DebugDrawingBatcher>();

    /// <summary>
    /// Get (or lazily create) a batcher instance for a given channel name.
    /// </summary>
    /// <param name="pName">
    /// Channel name used as a key. Defaults to "default".
    /// Use different names to segregate draw calls by subsystem.
    /// </param>
    /// <returns>
    /// A <see cref="DebugDrawingBatcher"/> instance associated with <paramref name="pName"/>.
    /// </returns>
    /// <remarks>
    /// - The first created instance becomes <see cref="root"/> and is the only one whose <see cref="OnDrawGizmos"/>
    /// executes drawing. Subsequent instances only hold their own queues.
    /// - If no batcher exists for <paramref name="pName"/>, this will create a new GameObject in the scene named:
    /// "DebugDrawingBatcher_{pName}".
    /// - Typical usage
    /// <code>
    /// // Enqueue gizmo drawing from anywhere:
    /// DebugDrawingBatcher.GetInstance("door").BatchCall(() =>
    /// {
    ///     Gizmos.color = Color.red;
    ///     Gizmos.DrawSphere(doorPosition, 0.2f);
    /// });
    /// </code>
    /// </remarks>
    public static DebugDrawingBatcher GetInstance(string pName = "default")
    {
        // Fast path: return existing instance if already created.
        if (!instances.TryGetValue(pName, out var value))
        {
            // Lazy creation path: create a new scene object + component.
            instances[pName] = value = CreateInstance(pName);

            // First created instance becomes the root, which is responsible for invoking all batches.
            if (root == null) root = value;

            // Track for centralized draw iteration.
            debugDrawingBatchers.Add(value);
        }

        return value;
    }

    /// <summary>
    /// Creates a new GameObject and attaches a <see cref="DebugDrawingBatcher"/> component.
    /// </summary>
    /// <param name="pName">The channel name used to build the GameObject name.</param>
    /// <returns>The newly created batcher component.</returns>
    /// <remarks>
    /// This method does not set hide flags, parent transforms, or DontDestroyOnLoad.
    /// It creates a normal scene object.
    /// </remarks>
    private static DebugDrawingBatcher CreateInstance(string pName)
    {
        GameObject go = new GameObject("DebugDrawingBatcher_" + pName);
        DebugDrawingBatcher instance = go.AddComponent<DebugDrawingBatcher>();
        return instance;
    }

    /// <summary>
    /// The queue of draw calls (delegates) enqueued for this specific batcher/channel.
    /// </summary>
    /// <remarks>
    /// Each call should perform gizmo drawing only (e.g. <see cref="Gizmos"/> or editor-only debug visuals).
    /// The call is invoked from the root batcher’s <see cref="OnDrawGizmos"/> callback.
    /// </remarks>
    private List<Action> batchedCalls = new();

    /// <summary>
    /// Enqueue a draw call to be invoked during the next gizmo draw pass.
    /// </summary>
    /// <param name="action">
    /// Delegate containing gizmo drawing commands (e.g. <see cref="Gizmos.DrawLine"/>).
    /// </param>
    /// <remarks>
    /// - Calls are stored and will continue to be invoked every gizmo pass until cleared.
    /// - Keep delegates small and side-effect free. Avoid allocating heavily or doing expensive computation here.
    /// </remarks>
    public void BatchCall(Action action)
    {
        batchedCalls.Add(action);
    }

    /// <summary>
    /// Clears all currently enqueued calls for this batcher/channel.
    /// </summary>
    /// <remarks>
    /// This only clears the queue on the specific instance it is called on.
    /// Other named batchers retain their queued calls unless they are cleared separately.
    /// </remarks>
    public void ClearAllBatchedCalls()
    {
        batchedCalls.Clear();
    }

    /// <summary>
    /// Unity gizmo callback. Executes all queued calls across all batchers, but only on the root instance.
    /// </summary>
    /// <remarks>
    /// Unity may call this multiple times per frame depending on Scene View/Game View, editor state, etc.
    /// This class does not attempt to deduplicate between passes.
    /// <para/>
    /// The root-only check ensures that the aggregated drawing runs once from a single component,
    /// even though multiple batcher GameObjects exist.
    /// </remarks>
    private void OnDrawGizmos()
    {
        // Only the designated root executes the aggregated drawing.
        if (this != root) return;

        // Execute every enqueued call across all batchers.
        foreach (var batcher in debugDrawingBatchers)
        {
            foreach (var call in batcher.batchedCalls)
            {
                call.Invoke();
            }
        }
    }

    /// <summary>
    /// Unity callback when the application is quitting.
    /// Clears static state so the batcher can be reinitialized cleanly on the next run.
    /// </summary>
    /// <remarks>
    /// In the Unity Editor, domain reload and play mode transitions may not behave exactly like a player quit.
    /// This method is primarily relevant for standalone builds.
    /// </remarks>
    private void OnApplicationQuit()
    {
        Debug.Log("Quitting");

        instances.Clear();
        debugDrawingBatchers.Clear();
        root = null;
    }
}
