using System;
using System.Collections.Generic;

namespace Core.FSM
{
    /// <summary>
    /// Generic, engine-agnostic state machine. States are registered under an
    /// id (typically an enum value); <see cref="SetState"/> switches between
    /// them with Exit/Enter calls and <see cref="Tick"/> forwards to the
    /// current state. Not a MonoBehaviour — the owner decides when to tick.
    ///
    /// Transition log: subscribe to <see cref="OnTransition"/> (from, to).
    /// The machine itself never logs, so it stays free of engine references.
    /// </summary>
    public class StateMachine<TId>
    {
        readonly Dictionary<TId, IState> states = new Dictionary<TId, IState>();

        public TId    CurrentId    { get; private set; }
        public IState CurrentState { get; private set; }

        /// <summary>Raised after every completed transition: (fromId, toId).</summary>
        public event Action<TId, TId> OnTransition;

        public void Add(TId id, IState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            states[id] = state;
        }

        /// <summary>
        /// Switches to the given state (Exit old → Enter new). Re-requesting
        /// the state that is already current is a no-op, mirroring what a
        /// plain "State = x" field assignment used to do.
        /// </summary>
        public void SetState(TId id)
        {
            if (CurrentState != null && EqualityComparer<TId>.Default.Equals(CurrentId, id))
                return;

            if (!states.TryGetValue(id, out IState next))
                throw new KeyNotFoundException($"StateMachine: no state registered for id '{id}'.");

            TId from = CurrentId;
            CurrentState?.Exit();
            CurrentId    = id;
            CurrentState = next;
            next.Enter();
            OnTransition?.Invoke(from, id);
        }

        /// <summary>Ticks the current state. Safe to call before the first SetState.</summary>
        public void Tick(float deltaTime) => CurrentState?.Tick(deltaTime);
    }
}
