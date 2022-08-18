using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace UIFramework.Core
{
    /// <summary>
    /// UI管理基类
    /// 在C#侧实现的好处是可以同时管理Lua和C#实现的UI逻辑
    /// </summary>
    /// <typeparam name="TUIObj">UGUI:GameObject or FairyGUI:GObject</typeparam>
    public abstract class UIManagerBase<TUIObj>
    {
        protected class Window
        {
            public string Name;
            public bool IsUpdatable;
            public bool IsBackground;
            public Func<IWindow<TUIObj>> Create;
            public string[] Dependencies;

            public IWindow<TUIObj> Inst;
            public int Layer = -1;
            public float SoringOrder;
            public bool IsActive;
            public bool IsLoaded;

            /// <summary>
            /// 是否处于正在关闭
            /// </summary>
            public bool IsClosing;

            /// <summary>
            /// 是否处于正在销毁
            /// </summary>
            public bool IsDestroying;
        }

        private readonly List<string> windowStack = new List<string>();
        private readonly List<Window> updatableWindows = new List<Window>();
        private readonly Dictionary<string, Window> windowsDict = new Dictionary<string, Window>();
        private readonly List<string> mutexWindows = new List<string>();

        public virtual void Init()
        {
        }

        public virtual void RegisterLuaWindow(string name, string moduleName, Type type, bool isBackground,
            params string[] dependencies)
        {
#if UNITY_EDITOR
            Debug.Assert(type.IsSubclassOf(typeof(LuaWindowBase<TUIObj>)));
#endif
            RegisterWindow(name, () => (LuaWindowBase<TUIObj>) Activator.CreateInstance(type, moduleName), isBackground,
                dependencies);
        }

        public virtual void RegisterLuaWindow<T>(string name, string moduleName, bool isBackground,
            params string[] dependencies)
        {
            var type = typeof(T);
#if UNITY_EDITOR
            Debug.Assert(type.IsSubclassOf(typeof(LuaWindowBase<TUIObj>)));
#endif
            RegisterWindow(name, () => (LuaWindowBase<TUIObj>) Activator.CreateInstance(type, moduleName), isBackground,
                dependencies);
        }

        public virtual void RegisterWindow(string name, Type type, bool isBackground,
            params string[] dependencies)
        {
#if UNITY_EDITOR
            Debug.Assert(typeof(IWindow<TUIObj>).IsAssignableFrom(type));
            Debug.Assert(type.GetConstructor(new Type[0]) != null);
#endif
            RegisterWindow(name, () => (IWindow<TUIObj>) Activator.CreateInstance(type), isBackground, dependencies);
        }

        /// <summary>
        /// 注册窗口信息
        /// </summary>
        /// <param name="name"></param>
        /// <param name="createFunc"></param>
        /// <param name="isBackground">是否为背景UI</param>
        /// <param name="dependencies">该窗口的依赖资源</param>
        public void RegisterWindow(string name, Func<IWindow<TUIObj>> createFunc, bool isBackground,
            params string[] dependencies)
        {
#if UNITY_EDITOR
            if (windowsDict.ContainsKey(name))
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

            windowsDict[name] = window;
        }

        /// <summary>
        /// 打开窗口
        /// </summary>
        /// <param name="name"></param>
        /// <param name="layer">窗口层级</param>
        /// <param name="closeOthers">打开窗口前关闭其他窗口</param>
        /// <param name="popWindow">是否弹出在该窗口打开期间的弹窗</param>
        public void OpenWindow(string name, int layer, bool closeOthers = false, bool popWindow = false)
        {
            if (windowsDict.TryGetValue(name, out var window))
            {
                if (window.IsActive || window.IsClosing || window.IsDestroying)
                    return;

                if (!window.IsLoaded)
                {
                    Debug.Assert(window.Create != null, "Failed to create window !!!");
                    var inst = window.Create();
                    var type = inst.GetType();
                    const string onupdate = "OnUpdate";
                    var methodInfo = type.GetMethod(onupdate, BindingFlags.Instance | BindingFlags.Public);
                    // 是否重写基类的OnUpdate
                    var baseMethodInfo =
                        type.BaseType?.GetMethod(onupdate, BindingFlags.Instance | BindingFlags.Public);
                    if (methodInfo.DeclaringType != baseMethodInfo?.DeclaringType)
                        //重写过OnUpdate的子类说明需要每帧调用OnUpdate
                        window.IsUpdatable = true;

                    var uiObj = Instantiate(window.Name, window.Dependencies);
                    inst.OnCreate(uiObj);
                    Deactivate(uiObj);

                    window.Inst = inst;
                    window.IsLoaded = true;

                    if (window.IsUpdatable)
                        updatableWindows.Add(window);
                }

                if (window.Layer != layer)
                    AddWindowToLayer(window.Inst.ui, layer);

                window.Layer = layer;

                if (closeOthers)
                {
                    var stackCount = windowStack.Count;
                    for (int i = 0; i < stackCount; i++)
                    {
                        if (!windowStack[i].Equals(name))
                        {
                            CloseWindow(windowStack[i], false, false, false);
                        }
                    }
                }

                InternalOpen(window);

                var index = windowStack.IndexOf(name);

                if (window.IsBackground)
                {
                    var lastBackgroundWindowIndex = GetLastBackgroundWindowIndex();

                    if (lastBackgroundWindowIndex < 0 ||
                        windowsDict[windowStack[lastBackgroundWindowIndex]].Name != name)
                    {
                        if (index >= 0)
                        {
                            var beginIndex = index + 1;
                            var nextBackgroundWindowIndex = lastBackgroundWindowIndex;
                            for (var i = beginIndex; i < windowStack.Count; nextBackgroundWindowIndex = i++)
                            {
                                if (windowsDict[windowStack[i]].IsBackground)
                                    break;
                            }

                            if (popWindow)
                            {
                                PopWindowStack(beginIndex, nextBackgroundWindowIndex);
                            }
                            else
                            {
                                ClearWindowStack(beginIndex, nextBackgroundWindowIndex);
                            }
                        }
                        else
                        {
                            windowStack.Add(name);
                        }
                    }
                    else if (popWindow)
                    {
                        PopWindowStack(lastBackgroundWindowIndex + 1, windowStack.Count - 1);
                    }
                    else
                    {
                        ClearWindowStack(lastBackgroundWindowIndex + 1, windowStack.Count - 1);
                    }
                }
                else
                {
                    if (index < 0)
                        windowStack.Add(name);
                }
            }
            else
            {
                Debug.LogError($"Failed open window {name} !!!");
            }
        }
        
        /// <summary>
        /// 关闭窗口
        /// </summary>
        /// <param name="name"></param>
        /// <param name="removeWindowStack">是否将该窗口移除缓存队列</param>
        /// <param name="removeWindowStackOnlyTop">当且仅当该窗口是最后一个时移除</param>
        /// <param name="popMutexWindow">弹出在排队的互斥窗口</param>
        /// <returns></returns>
        public async Task CloseWindow(string name, bool removeWindowStack = true,
            bool removeWindowStackOnlyTop = true, bool popMutexWindow = true)
        {
            if (windowsDict.TryGetValue(name, out var window))
            {
                if (!window.IsLoaded || !window.IsActive || window.IsClosing || window.IsDestroying)
                    return;

                if (removeWindowStack)
                {
                    int index;
                    if ((index = windowStack.IndexOf(name)) >= 0)
                    {
                        if (!removeWindowStackOnlyTop || index == windowStack.Count - 1)
                        {
                            windowStack.RemoveAt(index);
                        }
                    }
                }

                await InternalClose(window);
            }
        }

        public void CloseAllWindows(bool removeWindowCache = true)
        {
            for (int i = windowStack.Count - 1; i >= 0; i--)
#pragma warning disable 4014
                CloseWindow(windowStack[i], removeWindowCache);
#pragma warning restore 4014
        }

        public void CloseAllWindows(int layer, bool removeWindowCache = true)
        {
            for (int i = windowStack.Count - 1; i >= 0; i--)
                if (windowsDict[windowStack[i]].Layer == layer)
#pragma warning disable 4014
                    CloseWindow(windowStack[i], removeWindowCache);
#pragma warning restore 4014
        }

        public void CloseAllWindowsExceptLayer(int layer, bool removeWindowCache = true)
        {
            for (int i = windowStack.Count - 1; i >= 0; i--)
                if (windowsDict[windowStack[i]].Layer != layer)
#pragma warning disable 4014
                    CloseWindow(windowStack[i], removeWindowCache);
#pragma warning restore 4014
        }

        public async Task DestroyWindow(string name)
        {
            if (windowsDict.TryGetValue(name, out var window))
            {
                if (!window.IsLoaded || window.IsDestroying)
                    return;

                window.IsDestroying = true;

                windowStack.Remove(name);
                updatableWindows.Remove(window);

                if (!window.IsClosing)
                {
                    await InternalClose(window);
                }
                else
                {
                    void WaitForWindowClosed()
                    {
                        while (window.IsClosing) Task.Delay(1);
                    }

                    await Task.Run(WaitForWindowClosed);
                }

                var ui = window.Inst.ui;
                window.Inst.OnDestroy();
                Destroy(ui, window.Name, window.Dependencies);
                window.Inst = null;
                window.Layer = -1;
                window.IsLoaded = false;
                window.IsDestroying = false;
            }
        }

        public void DestroyAllWindow()
        {
            for (var i = windowStack.Count - 1; i >= 0; i--)
#pragma warning disable 4014
                DestroyWindow(windowStack[i]);
#pragma warning restore 4014
        }

        public void DestroyAllWindow(int layer)
        {
            for (var i = windowStack.Count - 1; i >= 0; i--)
                if (windowsDict[windowStack[i]].Layer == layer)
#pragma warning disable 4014
                    DestroyWindow(windowStack[i]);
#pragma warning restore 4014
        }

        public void DestroyAllWindowExceptLayer(int layer)
        {
            for (var i = windowStack.Count - 1; i >= 0; i--)
                if (windowsDict[windowStack[i]].Layer != layer)
#pragma warning disable 4014
                    DestroyWindow(windowStack[i]);
#pragma warning restore 4014
        }

        public void SendMessage(string name, int messageId, object param)
        {
            if (windowsDict.TryGetValue(name, out var window))
            {
                if (window.Inst == null)
                {
                    Debug.LogError($"Failed handle message !!! {name} is null");
                    return;
                }

                window.Inst.HandleMessage(messageId, param);
            }
        }

        public void RemoveWindowStack(string name) =>
            windowStack.Remove(name);

        public bool IsWindowActive(string name) =>
            windowsDict.TryGetValue(name, out var window) && window.IsActive;

        public virtual void Update()
        {
            var count = updatableWindows.Count;
            for (var i = count - 1; i >= 0; i--)
                updatableWindows[i].Inst.OnUpdate();
        }

        protected virtual void InternalOpen(Window window)
        {
            Activate(window.Inst.ui);
            window.IsActive = true;
            window.Inst.OnEnable();
        }

        protected virtual async Task InternalClose(Window window)
        {
            window.IsClosing = true;
            var ui = window.Inst.ui;
            var duration = PlayClosingAnimation(window.Name, ui);
            if (duration > 0) await Task.Delay(Mathf.CeilToInt(duration * 1000));
            window.Inst.OnDisable();
            Deactivate(ui);
            window.IsActive = false;
            window.IsClosing = false;
        }

        protected virtual int GetLastBackgroundWindowIndex()
        {
            for (var i = windowStack.Count - 1; i >= 0; i--)
                if (windowsDict[windowStack[i]].IsBackground)
                    return i;
            return -1;
        }

        protected virtual Window GetWindow(string name)
        {
            windowsDict.TryGetValue(name, out var window);
            return window;
        }

        /// <summary>
        /// WindowFactory is called when register new window
        /// </summary>
        /// <returns></returns>
        protected virtual Window WindowFactory() => new Window();

        private void PopWindowStack(int beginIndex, int endIndex)
        {
            var limit = windowStack.Count - 1;
            var min = Mathf.Clamp(Math.Min(beginIndex, endIndex), 0, limit);
            var max = Mathf.Clamp(Math.Max(beginIndex, endIndex), 0, limit);
            for (var i = min; i <= max; i++)
            {
                var name = windowStack[i];
                var window = windowsDict[name];
                OpenWindow(name, window.Layer, false, false);
            }
        }

        private void ClearWindowStack(int beginIndex, int endIndex)
        {
            var limit = windowStack.Count - 1;
            var min = Mathf.Clamp(Math.Min(beginIndex, endIndex), 0, limit);
            var max = Mathf.Clamp(Math.Max(beginIndex, endIndex), 0, limit);
            for (var i = max; i >= min; i--)
                windowStack.RemoveAt(i);
        }

        protected abstract void Activate(TUIObj ui);

        protected abstract void Deactivate(TUIObj ui);

        /// <summary>
        /// 播放窗口关闭动画
        /// 若返回值小于0则表示窗口没有关闭动画
        /// </summary>
        /// <param name="name"></param>
        /// <param name="ui"></param>
        /// <returns>返回播放关闭动画的时间单位 秒</returns>
        protected virtual float PlayClosingAnimation(string name, TUIObj ui) => -1;

        /// <summary>
        /// UI的层级
        /// </summary>
        /// <param name="ui"></param>
        /// <param name="layer"></param>
        protected abstract void AddWindowToLayer(TUIObj ui, int layer);

        /// <summary>
        /// 根据项目实际资源加载框架重写创建UI的逻辑
        /// </summary>
        /// <param name="name"></param>
        /// <param name="dependencies"></param>
        /// <returns></returns>
        protected abstract TUIObj Instantiate(string name, string[] dependencies);

        /// <summary>
        /// 销毁UI物体卸载资源以及依赖资源(可选)
        /// </summary>
        /// <param name="ui"></param>
        /// <param name="name"></param>
        /// <param name="dependencies"></param>
        protected abstract void Destroy(TUIObj ui, string name, string[] dependencies);
    }
}