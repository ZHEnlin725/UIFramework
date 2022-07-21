namespace UIFramework.Core
{
    /// <summary>
    /// 由C#实现的UI逻辑继承该类
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class WindowBase<T> : IWindow<T>
    {
        private T _ui;
        public T ui => _ui;

        public virtual void OnCreate(T ui) { _ui = ui; }

        public virtual void OnEnable() { }

        public virtual void OnUpdate() { }

        public virtual void OnDisable() { }

        public virtual void OnDestroy() { _ui = default; }
        
        public virtual void HandleMessage(int messageId, object param) { }
    }
}