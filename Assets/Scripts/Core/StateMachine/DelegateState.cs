using System;

namespace Core.FSM
{
    /// <summary>
    /// IState built from delegates — lets an owner class keep its state logic
    /// as ordinary private methods and wire them into a StateMachine without
    /// one class per state. Any callback may be null.
    /// </summary>
    public class DelegateState : IState
    {
        readonly Action        onEnter;
        readonly Action<float> onTick;
        readonly Action        onExit;

        public DelegateState(Action onEnter = null, Action<float> onTick = null, Action onExit = null)
        {
            this.onEnter = onEnter;
            this.onTick  = onTick;
            this.onExit  = onExit;
        }

        public void Enter()               => onEnter?.Invoke();
        public void Tick(float deltaTime) => onTick?.Invoke(deltaTime);
        public void Exit()                => onExit?.Invoke();
    }
}
