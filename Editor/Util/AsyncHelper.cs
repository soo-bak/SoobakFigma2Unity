using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace SoobakFigma2Unity.Editor.Util
{
    /// <summary>
    /// Bridges async/await with Unity Editor's main thread.
    /// </summary>
    internal static class AsyncHelper
    {
        private static readonly ConcurrentQueue<Action> MainThreadQueue = new ConcurrentQueue<Action>();
        private static SynchronizationContext _unitySyncContext;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            _unitySyncContext = SynchronizationContext.Current;
            EditorApplication.update += PumpQueue;
        }

        private static void PumpQueue()
        {
            while (MainThreadQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }

        public static void RunOnMainThread(Action action)
        {
            if (SynchronizationContext.Current == _unitySyncContext)
                action();
            else
                MainThreadQueue.Enqueue(action);
        }

        /// <summary>
        /// Run an async task from Unity Editor context and pump results back to main thread.
        /// </summary>
        public static async void RunAsync(Func<Task> asyncFunc, Action<Exception> onError = null)
        {
            try
            {
                await asyncFunc();
            }
            catch (Exception e)
            {
                if (onError != null)
                    RunOnMainThread(() => onError(e));
                else
                    RunOnMainThread(() => UnityEngine.Debug.LogException(e));
            }
        }

        public static async void RunAsync<T>(Func<Task<T>> asyncFunc, Action<T> onComplete, Action<Exception> onError = null)
        {
            try
            {
                var result = await asyncFunc();
                RunOnMainThread(() => onComplete(result));
            }
            catch (Exception e)
            {
                if (onError != null)
                    RunOnMainThread(() => onError(e));
                else
                    RunOnMainThread(() => UnityEngine.Debug.LogException(e));
            }
        }
    }
}
