using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UIFramework.Core
{
    /// <summary>
    /// UI管理基类
    /// 在C#侧实现的好处是可以同时管理Lua和C#实现的UI逻辑
    /// </summary>
    /// <typeparam name="UI">UGUI:GameObject or FairyGUI:GObject</typeparam>
#if EXTENDS_MONO
    public abstract class UIManagerBase<UI> : MonoBehaviour
#else
    public abstract class UIManagerBase<UI>
#endif
    {
        protected class WindowInfo
        {
            public string Name;
            public bool IsUpdatable;
            public bool IsBackground;
            public Func<IWindow<UI>> Create;
            public string[] Dependencies;

            public IWindow<UI> Inst;
            public int Layer = -1;
            public float SoringOrder;
            public bool IsActive;
            public bool IsInstantiated;

            /// <summary>
            /// 是否处于正在关闭
            /// </summary>
            public bool IsClosing;

            /// <summary>
            /// 是否处于正在销毁
            /// </summary>
            public bool IsDestroying;

            public bool CloseImmediate;

            public bool DestroyImmediate;
        }

        private readonly List<string> windowStack = new List<string>();
        private readonly List<string> windowInsts = new List<string>();

        private readonly List<WindowInfo> updatableWindows = new List<WindowInfo>();
        private readonly Dictionary<string, WindowInfo> windowInfoDict = new Dictionary<string, WindowInfo>();

        private float stopwatchTimescale = 1;
        private readonly Stopwatch stopwatch = new Stopwatch();

        public virtual void RegisterLuaWindow<T>(string name, string moduleName, bool isBackground,
            params string[] dependencies) where T : LuaWindowBase<UI> =>
            RegisterLuaWindow(name, moduleName, typeof(T), isBackground, dependencies);

        public virtual void RegisterLuaWindow(string name, string moduleName, Type type, bool isBackground,
            params string[] dependencies)
        {
#if UNITY_EDITOR
            Debug.Assert(type.IsSubclassOf(typeof(LuaWindowBase<UI>)));
#endif
            RegisterWindow(name, () => (LuaWindowBase<UI>) Activator.CreateInstance(type, moduleName), isBackground,
                dependencies);
        }

        public void RegisterWindow(string name, Type type, bool isBackground, params string[] dependencies)
        {
#if UNITY_EDITOR
            Debug.Assert(typeof(IWindow<UI>).IsAssignableFrom(type));
            Debug.Assert(type.GetConstructor(new Type[0]) != null);
#endif
            RegisterWindow(name, () => (IWindow<UI>) Activator.CreateInstance(type), isBackground, dependencies);
        }

        public void RegisterWindow(string name, Func<IWindow<UI>> createFunc, bool isBackground,
            params string[] dependencies)
        {
#if UNITY_EDITOR
            if (windowInfoDict.ContainsKey(name))
            {
                Debug.LogError($"Duplicate window name {name} !!!");
                return;
            }
#endif
            var window = WindowFactory();
            window.Name = name;
            window.Create = createFunc;
            window.IsBackground = isBackground;
            window.Dependencies = dependencies;

            windowInfoDict[name] = window;
        }

        private readonly List<string> windowsBuffer = new List<string>();

        public void OpenWindow(string name, int layer, bool closeOthers = false, bool popupWindows = true)
        {
            if (windowInfoDict.TryGetValue(name, out var windowInfo))
            {
                if (windowInfo.IsActive || windowInfo.IsClosing || windowInfo.IsDestroying)
                    return;

                if (!windowInfo.IsInstantiated)
                {
                    Debug.Assert(windowInfo.Create != null, "Failed to create window !!!");
                    var inst = windowInfo.Create();
                    var type = inst.GetType();
                    const string onupdate = "OnUpdate";
                    var methodInfo = type.GetMethod(onupdate, BindingFlags.Instance | BindingFlags.Public);
                    var baseMethodInfo =
                        type.BaseType?.GetMethod(onupdate, BindingFlags.Instance | BindingFlags.Public);
                    if (methodInfo.DeclaringType != baseMethodInfo?.DeclaringType)
                        //重写过OnUpdate的子类说明需要每帧调用OnUpdate
                        windowInfo.IsUpdatable = true;

                    var ui = Instantiate(windowInfo.Name, windowInfo.Dependencies);
                    inst.OnCreate(ui);
                    Deactivate(ui);

                    windowInfo.Inst = inst;
                    windowInfo.IsInstantiated = true;

                    if (windowInfo.IsUpdatable)
                        updatableWindows.Add(windowInfo);
                    windowInsts.Add(name);
                }

                if (windowInfo.Layer != layer)
                    AddWindowToLayer(windowInfo, layer);

                windowInfo.Layer = layer;

                if (closeOthers) CloseAllWindowsImmediate(false);

                InternalOpen(windowInfo);

                var indexOfStack = windowStack.IndexOf(name);
                if (indexOfStack >= 0) windowStack.RemoveAt(indexOfStack);
                windowStack.Add(name);

                if (windowInfo.IsBackground)
                {
                    if (indexOfStack >= 0)
                    {
                        int from = indexOfStack, to = indexOfStack - 1;
                        for (var i = from; i < windowStack.Count; to = i++)
                        {
                            if (windowInfoDict[windowStack[i]].IsBackground)
                                break;
                        }

                        if (popupWindows)
                        {
                            windowsBuffer.Clear();
                            for (var i = from; i <= to; i++)
                                windowsBuffer.Add(windowStack[i]);
                            foreach (var winname in windowsBuffer)
                            {
                                windowInfoDict.TryGetValue(winname, out var info);
                                OpenWindow(info.Name, info.Layer, false, false);
                            }
                        }
                        else
                        {
                            for (var i = to; i >= from; i--)
                                windowStack.RemoveAt(i);
                        }
                    }
                }
            }
        }

        public async Task CloseWindow(string name, bool removeWindowStack = false, bool removeWindowStackOnlyTop = true,
            bool openLastClosedBgWin = true)
        {
            if (windowInfoDict.TryGetValue(name, out var windowInfo))
            {
                if (!windowInfo.IsInstantiated || !windowInfo.IsActive || windowInfo.IsClosing ||
                    windowInfo.IsDestroying)
                    return;

                var indexOfStack = windowStack.IndexOf(name);
                if (removeWindowStack)
                {
                    if (indexOfStack >= 0)
                    {
                        if (!removeWindowStackOnlyTop || indexOfStack == windowStack.Count - 1)
                        {
                            windowStack.RemoveAt(indexOfStack);
                        }
                    }
                }

                await InternalClose(windowInfo);

                if (openLastClosedBgWin && windowInfo.IsBackground)
                {
                    OpenLastClosedBgWindow(indexOfStack - 1, true);
                }
            }
        }

        public void CloseWindowImmediate(string name, bool removeWindowStack = false,
            bool removeWindowStackOnlyTop = true, bool openLastClosedBgWin = true)
        {
            if (windowInfoDict.TryGetValue(name, out var windowInfo))
            {
                if (!windowInfo.IsInstantiated || !windowInfo.IsActive || windowInfo.IsDestroying)
                    return;

                if (windowInfo.IsClosing && windowInfo.CloseImmediate)
                {
                    return;
                }

                var indexOfStack = windowStack.IndexOf(name);

                if (removeWindowStack)
                {
                    if (indexOfStack >= 0)
                    {
                        if (!removeWindowStackOnlyTop || indexOfStack == windowStack.Count - 1)
                        {
                            windowStack.RemoveAt(indexOfStack);
                        }
                    }
                }

                InternalCloseImmediate(windowInfo);

                if (openLastClosedBgWin && windowInfo.IsBackground)
                {
                    OpenLastClosedBgWindow(indexOfStack - 1, true);
                }
            }
        }

        public async Task DestroyWindow(string name, bool removeWindowStack = false,
            bool removeWindowStackOnlyTop = true, bool openLastClosedBgWin = true)
        {
            if (windowInfoDict.TryGetValue(name, out var windowInfo))
            {
                if (!windowInfo.IsInstantiated || windowInfo.IsDestroying)
                    return;

                windowInsts.Remove(name);
                updatableWindows.Remove(windowInfo);

                windowInfo.IsDestroying = true;

                windowInfo.DestroyImmediate = false;

                var indexOfStack = windowStack.IndexOf(name);
                if (removeWindowStack && indexOfStack >= 0)
                {
                    if (!removeWindowStackOnlyTop || indexOfStack == windowStack.Count - 1)
                    {
                        windowStack.RemoveAt(indexOfStack);
                    }
                }

                if (windowInfo.IsActive)
                {
                    if (windowInfo.IsClosing)
                    {
                        await Task.Run(() =>
                        {
                            while (windowInfo.IsClosing) ;
                        });
                    }
                    else
                    {
                        await InternalClose(windowInfo);
                    }
                }

                if (windowInfo.DestroyImmediate) return;

                InternalDestroy(windowInfo);

                if (openLastClosedBgWin && windowInfo.IsBackground)
                {
                    OpenLastClosedBgWindow(indexOfStack - 1, true);
                }
            }
        }

        public void DestroyWindowImmediate(string name, bool removeWindowStack = false,
            bool removeWindowStackOnlyTop = true, bool openLastClosedBgWin = true)
        {
            if (windowInfoDict.TryGetValue(name, out var windowInfo))
            {
                if (!windowInfo.IsInstantiated)
                    return;

                if (windowInfo.IsDestroying)
                {
                    if (windowInfo.DestroyImmediate)
                    {
                        return;
                    }
                }

                windowInsts.Remove(name);
                updatableWindows.Remove(windowInfo);

                var indexOfStack = windowStack.IndexOf(name);
                if (removeWindowStack && indexOfStack >= 0)
                {
                    if (!removeWindowStackOnlyTop || indexOfStack == windowStack.Count - 1)
                    {
                        windowStack.RemoveAt(indexOfStack);
                    }
                }

                windowInfo.IsDestroying = true;

                windowInfo.DestroyImmediate = true;

                if (windowInfo.IsActive)
                    InternalCloseImmediate(windowInfo);

                InternalDestroy(windowInfo);

                if (openLastClosedBgWin && windowInfo.IsBackground)
                {
                    OpenLastClosedBgWindow(indexOfStack - 1, true);
                }
            }
        }

        public async void CloseAllWindows(bool removeWindowStack = true)
        {
            for (var i = windowStack.Count - 1; i >= 0; i--)
                if (i <= windowStack.Count - 1) //此判断避免嵌套调用引起索引越界
                    await CloseWindow(windowStack[i], removeWindowStack, false, false);
        }

        public void CloseAllWindowsImmediate(bool removeWindowStack = true)
        {
            for (var i = windowStack.Count - 1; i >= 0; i--)
                if (i <= windowStack.Count - 1) //此判断避免嵌套调用引起索引越界
                    CloseWindowImmediate(windowStack[i], removeWindowStack, false, false);
        }

        public void DestroyAllWindows()
        {
            for (var i = windowStack.Count - 1; i >= 0; i--)
                if (i <= windowStack.Count - 1) //此判断避免嵌套调用引起索引越界
                    DestroyWindowImmediate(windowStack[i], true, false, false);
        }

        public void SendMessage(string name, int messageId, object param)
        {
            if (windowInfoDict.TryGetValue(name, out var windowInfo))
            {
                if (windowInfo.Inst == null)
                {
                    Debug.LogError($"Failed handle message !!! {name} is null");
                    return;
                }

                windowInfo.Inst.HandleMessage(messageId, param);
            }
        }

        public void Broadcast(int messageId, object param)
        {
            var cnt = windowInsts.Count;
            for (var i = cnt - 1; i >= 0; i--)
            {
                windowInfoDict.TryGetValue(windowInsts[i], out var windowInfo);
                windowInfo.Inst.HandleMessage(messageId, param);
            }
        }

        public bool RemoveWindowStack(string name) =>
            windowStack.Remove(name);

        public bool IsWindowActive(string name) =>
            windowInfoDict.TryGetValue(name, out var windowInfo) && windowInfo.IsActive;

        public virtual void Update()
        {
            stopwatchTimescale = Mathf.Max(Time.timeScale, 0);
            var cnt = updatableWindows.Count;
            for (var i = cnt - 1; i >= 0; i--)
                updatableWindows[i].Inst.OnUpdate();
        }

        protected virtual WindowInfo GetWindow(string name)
        {
            windowInfoDict.TryGetValue(name, out var windowInfo);
            return windowInfo;
        }

        protected virtual void OpenLastClosedBgWindow(int index, bool popWindow)
        {
            for (int i = index; i >= 0; i--)
            {
                windowInfoDict.TryGetValue(windowStack[i], out var windowInfo);
                if (windowInfo.IsBackground)
                {
                    OpenWindow(windowInfo.Name, windowInfo.Layer, false, popWindow);
                    break;
                }
            }
        }

        protected virtual void InternalOpen(WindowInfo windowInfo)
        {
            Activate(windowInfo.Inst.ui);
            windowInfo.IsActive = true;
            windowInfo.Inst.OnEnable();
        }

        protected virtual async Task InternalClose(WindowInfo windowInfo)
        {
            windowInfo.IsClosing = true;
            windowInfo.CloseImmediate = false;
            var ui = windowInfo.Inst.ui;
            float duration;
            if ((duration = PlayCloseAnimation(windowInfo.Name, ui)) > 0)
            {
                if (!stopwatch.IsRunning) stopwatch.Start();
                await Task.Run(() =>
                {
                    var durationMilliseconds = duration * 1000;
                    for (var timeSinceClose = stopwatch.ElapsedMilliseconds;
                        (stopwatch.ElapsedMilliseconds - timeSinceClose) * stopwatchTimescale < durationMilliseconds;)
                        if (windowInfo.CloseImmediate)
                            break;
                });
            }

            if (windowInfo.CloseImmediate) return;

            windowInfo.Inst.OnDisable();
            Deactivate(ui);
            windowInfo.IsActive = false;
            windowInfo.IsClosing = false;
        }

        protected virtual void InternalCloseImmediate(WindowInfo windowInfo)
        {
            windowInfo.IsClosing = true;
            windowInfo.CloseImmediate = true;
            var ui = windowInfo.Inst.ui;
            windowInfo.Inst.OnDisable();
            Deactivate(ui);
            windowInfo.IsActive = false;
            windowInfo.IsClosing = false;
        }

        protected virtual void InternalDestroy(WindowInfo windowInfo)
        {
            var ui = windowInfo.Inst.ui;
            windowInfo.Inst.OnDestroy();
            Destroy(ui, windowInfo.Name, windowInfo.Dependencies);
            windowInfo.Inst = null;
            windowInfo.Layer = -1;
            windowInfo.IsInstantiated = false;
            windowInfo.IsDestroying = false;
        }

        protected virtual float PlayCloseAnimation(string name, UI ui) => -1;

        protected virtual void AddWindowToLayer(WindowInfo windowInfo, int layer)
        {
        }

        protected abstract void Activate(UI ui);

        protected abstract void Deactivate(UI ui);

        protected abstract UI Instantiate(string name, string[] dependencies);

        protected abstract void Destroy(UI ui, string name, string[] dependencies);

        protected virtual WindowInfo WindowFactory() => new WindowInfo();
    }
}