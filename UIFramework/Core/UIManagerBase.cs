using System;
using System.Collections.Generic;
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

            public float ActiveTicks;

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

        private readonly List<WindowInfo> windowInsts = new List<WindowInfo>();
        private readonly List<WindowInfo> updatableWindows = new List<WindowInfo>();
        private readonly Dictionary<string, WindowInfo> windowInfoDict = new Dictionary<string, WindowInfo>();

        protected event Action<WindowInfo> cachedInternalClose, cachedInternalCloseDestroy;

        protected const float DestroyThresholdSeconds = 60;

        public virtual void Init()
        {
            // 关闭时 播放关闭动画完毕时调用
            cachedInternalClose = InternalClose;

            // 销毁时 播放关闭动画完毕时调用
            cachedInternalCloseDestroy = InternalClose;
            cachedInternalCloseDestroy += InternalDestroy;
        }

        /// <summary>
        /// 注册Lua Window
        /// </summary>
        /// <param name="name">UI名称</param>
        /// <param name="moduleName">Lua模块名称</param>
        /// <param name="isBackground">是否是背景UI</param>
        /// <param name="dependencies">依赖资源可以是图集,资源包根据Instantiate具体实现来加载</param>
        /// <typeparam name="T"></typeparam>
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

        /// <summary>
        /// 可用于注册C# Lua Window
        /// </summary>
        /// <param name="name"></param>
        /// <param name="type">窗口类型</param>
        /// <param name="isBackground"></param>
        /// <param name="dependencies"></param>
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

        /// <summary>
        /// 打开界面
        /// </summary>
        /// <param name="name"></param>
        /// <param name="layer">所在层级 具体层级由子类管理</param>
        /// <param name="closeOthers">是否关闭其他界面</param>
        /// <param name="popupWindows">是否弹出此界面关闭时 未关闭的子(非背景)界面</param>
        public void OpenWindow(string name, int layer, bool closeOthers = false, bool popupWindows = true)
        {
            if (windowInfoDict.TryGetValue(name, out var windowInfo))
            {
                if (windowInfo.IsActive || windowInfo.IsClosing || windowInfo.IsDestroying)
                    return;

                if (!windowInfo.IsInstantiated)
                {
                    Debug.Assert(windowInfo.Create != null,
                        $"Failed to create window !!! Create Func Is Null [{windowInfo.Name}]");
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
                }

                windowInsts.Remove(windowInfo);
                windowInsts.Add(windowInfo);

                windowInfo.ActiveTicks = Time.time;

                AddWindowToLayer(windowInfo, layer);

                windowInfo.Layer = layer;

                if (closeOthers) CloseAllWindows(false);

                InternalOpen(windowInfo);

                var index = windowStack.IndexOf(name);
                if (index >= 0) windowStack.RemoveAt(index);
                windowStack.Add(name);

                if (windowInfo.IsBackground)
                {
                    if (index >= 0)
                    {
                        int from = index, to = index - 1;
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
                                var info = windowInfoDict[winname];
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="removeWindowStack">移除窗口栈</param>
        /// <param name="removeWindowStackOnlyTop">removeWindowStack=true时 若该选项为true则仅移除顶层UI</param>
        /// <param name="openLastClosedBgWin">若关闭的界面是背景UI则打开上一个关闭的背景界面</param>
        /// <param name="playCloseAnim">是否播放关闭动画</param>
        public void CloseWindow(string name, bool removeWindowStack = true, bool removeWindowStackOnlyTop = true,
            bool openLastClosedBgWin = true, bool playCloseAnim = true)
        {
            if (windowInfoDict.TryGetValue(name, out var windowInfo))
            {
                if (!windowInfo.IsInstantiated || !windowInfo.IsActive || windowInfo.IsClosing ||
                    windowInfo.IsDestroying)
                    return;

                windowInfo.IsClosing = true;

                var index = windowStack.IndexOf(name);

                if (removeWindowStack)
                {
                    if (index >= 0)
                    {
                        if (!removeWindowStackOnlyTop || index == windowStack.Count - 1)
                        {
                            windowStack.RemoveAt(index);
                            if (windowInfo.IsBackground)
                            {
                                for (int i = index; i < windowStack.Count; i++)
                                {
                                    if (!windowInfoDict[windowStack[i]].IsBackground)
                                    {
                                        windowStack.RemoveAt(i--);
                                    }
                                }
                            }
                        }
                    }
                }

                if (openLastClosedBgWin && windowInfo.IsBackground)
                {
                    var flag = true;
                    var cnt = windowStack.Count;
                    for (int i = index + 1; i < cnt; i++)
                    {
                        if (windowInfoDict[windowStack[i]].IsBackground)
                        {
                            flag = false;
                            break;
                        }
                    }

                    if (flag)
                    {
                        OpenLastClosedBgWindow(index - 1, true);
                    }
                }

                if (playCloseAnim)
                {
                    PlayCloseAnimation(windowInfo, cachedInternalClose);
                }
                else
                {
                    InternalClose(windowInfo);
                }
            }
        }

        /// <summary>
        /// 销毁界面 与CloseWindow类似但是会移除updatableWindows列表、释放资源
        /// </summary>
        /// <param name="name"></param>
        /// <param name="removeWindowStack"></param>
        /// <param name="removeWindowStackOnlyTop"></param>
        /// <param name="openLastClosedBgWin"></param>
        /// <param name="playCloseAnim"></param>
        public async void DestroyWindow(string name, bool removeWindowStack = true,
            bool removeWindowStackOnlyTop = true, bool openLastClosedBgWin = true, bool playCloseAnim = true)
        {
            if (windowInfoDict.TryGetValue(name, out var windowInfo))
            {
                if (!windowInfo.IsInstantiated || windowInfo.IsDestroying)
                    return;

                windowInsts.Remove(windowInfo);
                updatableWindows.Remove(windowInfo);

                windowInfo.IsDestroying = true;

                var index = windowStack.IndexOf(name);

                if (removeWindowStack)
                {
                    if (index >= 0)
                    {
                        if (!removeWindowStackOnlyTop || index == windowStack.Count - 1)
                        {
                            windowStack.RemoveAt(index);
                            if (windowInfo.IsBackground)
                            {
                                for (int i = index; i < windowStack.Count; i++)
                                {
                                    if (!windowInfoDict[windowStack[i]].IsBackground)
                                    {
                                        windowStack.RemoveAt(i--);
                                    }
                                }
                            }
                        }
                    }
                }

                if (openLastClosedBgWin && windowInfo.IsBackground)
                {
                    var flag = true;
                    var cnt = windowStack.Count;
                    for (int i = index + 1; i < cnt; i++)
                    {
                        if (windowInfoDict[windowStack[i]].IsBackground)
                        {
                            flag = false;
                            break;
                        }
                    }

                    if (flag)
                    {
                        OpenLastClosedBgWindow(index - 1, true);
                    }
                }

                if (windowInfo.IsActive)
                {
                    if (windowInfo.IsClosing)
                    {
                        await Task.Run(() =>
                        {
                            while (windowInfo.IsClosing)
                            {
                                // ignore
                            }
                        });
                    }
                    else
                    {
                        windowInfo.IsClosing = true;

                        if (playCloseAnim)
                        {
                            PlayCloseAnimation(windowInfo, cachedInternalCloseDestroy);
                            return;
                        }

                        InternalClose(windowInfo);
                    }
                }

                InternalDestroy(windowInfo);
            }
        }

        public void CloseAllWindows(bool removeWindowStack = true)
        {
            for (var i = windowStack.Count - 1; i >= 0; i--)
                if (i <= windowStack.Count - 1) //此判断避免嵌套调用引起索引越界
                    CloseWindow(windowStack[i], removeWindowStack, false, false, false);
        }

        public void DestroyAllWindows()
        {
            for (var i = windowStack.Count - 1; i >= 0; i--)
                if (i <= windowStack.Count - 1) //此判断避免嵌套调用引起索引越界
                    DestroyWindow(windowStack[i], true, false, false, false);
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
                windowInsts[i].Inst.HandleMessage(messageId, param);
        }

        public bool RemoveWindowStack(string name) =>
            windowStack.Remove(name);

        public bool IsWindowActive(string name) =>
            windowInfoDict.TryGetValue(name, out var windowInfo) && windowInfo.IsActive;

        public virtual void Update()
        {
            var cnt = updatableWindows.Count;
            for (var i = cnt - 1; i >= 0; i--)
                updatableWindows[i].Inst.OnUpdate();

            for (int i = 0; i < windowInsts.Count; i++)
            {
                var windowInfo = windowInsts[i];
                if (!windowInfo.IsActive && (Time.time - windowInfo.ActiveTicks) > DestroyThresholdSeconds)
                {
                    DestroyWindow(windowInfo.Name, false, false, false, false);
                    i--;
                }
            }
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
                var windowInfo = windowInfoDict[windowStack[i]];
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

        protected virtual void InternalClose(WindowInfo windowInfo)
        {
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
            windowInfo.IsInstantiated = false;
            windowInfo.IsDestroying = false;
        }

        /// <summary>
        /// 播放关闭UI动画 具体实现由子类实现
        /// 完毕后手动调用 close(info)回调
        /// </summary>
        /// <param name="info"></param>
        /// <param name="close"></param>
        protected virtual void PlayCloseAnimation(WindowInfo info, Action<WindowInfo> close) =>
            close(info);

        /// <summary>
        /// 添加UI到指定层级 具体层级由子类实现
        /// </summary>
        /// <param name="windowInfo"></param>
        /// <param name="layer"></param>
        protected virtual void AddWindowToLayer(WindowInfo windowInfo, int layer)
        {
        }

        /// <summary>
        /// 激活、显示 UI  
        /// </summary>
        /// <param name="ui"></param>
        protected abstract void Activate(UI ui);

        /// <summary>
        /// 关闭、隐藏 UI
        /// </summary>
        /// <param name="ui"></param>
        protected abstract void Deactivate(UI ui);

        /// <summary>
        /// 加载UI物体
        /// </summary>
        /// <param name="name">ui名称</param>
        /// <param name="dependencies">依赖资源</param>
        /// <returns></returns>
        protected abstract UI Instantiate(string name, string[] dependencies);

        /// <summary>
        /// 销毁UI物体
        /// </summary>
        /// <param name="name"></param>
        /// <param name="dependencies"></param>
        /// <returns></returns>
        protected abstract void Destroy(UI ui, string name, string[] dependencies);

        /// <summary>
        /// 创建WindowInfo实例
        /// 若子类需要为WindowInfo添加新字段
        /// 可继承WindowInfo重写该方法
        /// </summary>
        /// <returns></returns>
        protected virtual WindowInfo WindowFactory() => new WindowInfo();
    }
}