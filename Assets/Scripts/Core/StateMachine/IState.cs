// ---- Core.FSM — engine-agnostic state machine (reusable across projects) ----
// No UnityEngine, no game code: copy the Core/StateMachine folder into any
// C# project and it compiles as-is.

namespace Core.FSM
{
    /// <summary>
    /// One state in a <see cref="StateMachine{TId}"/>. Enter fires when the
    /// machine switches into the state, Tick every update while it is current,
    /// Exit when the machine switches away.
    /// </summary>
    public interface IState
    {
        void Enter();
        void Tick(float deltaTime);
        void Exit();
    }
}
