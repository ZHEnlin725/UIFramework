namespace UIFramework.Core
{
    public interface IWindow<TUIObj>
    {
        /// <summary>
        /// UI Object
        /// </summary>
        TUIObj ui { get; }

        /// <summary>
        /// OnCreate is called after instantiate
        /// </summary>
        void OnCreate(TUIObj ui);

        /// <summary>
        /// OnEnable is called after activate uiObj
        /// </summary>
        void OnEnable();

        /// <summary>
        /// OnUpdate is called once per frame if override
        /// </summary>
        void OnUpdate();

        /// <summary>
        /// OnDisable is called before deactivate uiObj
        /// </summary>
        void OnDisable();

        /// <summary>
        /// OnDestroy is called before destroy uiObj
        /// </summary>
        void OnDestroy();

        /// <summary>
        /// HandleMessage is called when UIManager.SendMessage invoked
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="param"></param>
        void HandleMessage(int messageId, object param);
    }
}