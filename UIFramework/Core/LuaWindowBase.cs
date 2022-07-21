using System;

namespace UIFramework.Core
{
    /// <summary>
    /// Lua窗口基类
    /// 根据实际使用的Lua框架重写
    /// </summary>
    /// <typeparam name="TUIObj"></typeparam>
    public abstract class LuaWindowBase<TUIObj> : WindowBase<TUIObj>
    {
        protected object luaTable;

        protected Action<object, object> createCallback;

        protected Action<object> enableCallback;
        protected Action<object> updateCallback;
        protected Action<object> disableCallback;
        protected Action<object> destroyCallback;
        protected Action<object, int, object> handleMessageCallback;

        protected LuaWindowBase(string modulename)
        {
            luaTable = getLuaTable(modulename);
            createCallback = getCreateCallback();
            enableCallback = getEnableCallback();
            updateCallback = getUpdateCallback();
            disableCallback = getDisableCallback();
            destroyCallback = getDestoryCallback();
            handleMessageCallback = getHandleMessageCallback();
        }

        ~LuaWindowBase()
        {
            if (luaTable is IDisposable disposable)
                disposable.Dispose();
            luaTable = null;
            createCallback = null;
            enableCallback = null;
            updateCallback = null;
            disableCallback = null;
            destroyCallback = null;
            handleMessageCallback = null;
        }

        public override void OnCreate(TUIObj ui)
        {
            base.OnCreate(ui);

            createCallback?.Invoke(luaTable, ui);
        }

        public override void OnEnable()
        {
            base.OnEnable();

            enableCallback?.Invoke(luaTable);
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            updateCallback?.Invoke(luaTable);
        }

        public override void OnDisable()
        {
            disableCallback?.Invoke(luaTable);

            base.OnDisable();
        }

        public override void OnDestroy()
        {
            destroyCallback?.Invoke(luaTable);

            base.OnDestroy();
        }

        public override void HandleMessage(int messageId, object param)
        {
            base.HandleMessage(messageId, param);

            handleMessageCallback?.Invoke(luaTable, messageId, param);
        }

        protected abstract object getLuaTable(string module);

        protected abstract Action<object, object> getCreateCallback();
        protected abstract Action<object> getEnableCallback();
        protected abstract Action<object> getDisableCallback();
        protected abstract Action<object> getUpdateCallback();
        protected abstract Action<object> getDestoryCallback();
        protected abstract Action<object, int, object> getHandleMessageCallback();
    }
}