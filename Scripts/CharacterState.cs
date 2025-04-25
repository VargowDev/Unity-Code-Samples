using UnityEngine;

namespace TheRevenantEngine.Characters
{
    public enum CharacterStateType { Idle, Patrol, Chase, Attack, Dead }

    /// <summary>
    /// Simple state machine controller for enemy behavior.
    /// Manages transitions between states like Patrol, Chase, Attack, and Dead,
    /// and triggers movement, attack logic, and animations accordingly.
    /// </summary>
    public class CharacterState : MonoBehaviour
    {
        private Character character;
        private CharacterAnimator characterAnimator;
        private AttackHandler attackHandler;
        public CharacterStateType CurrentState { get; private set; }

        private void Awake()
        {
            character = GetComponent<Character>();
            characterAnimator = GetComponent<CharacterAnimator>();
            attackHandler = GetComponent<AttackHandler>();
        }
        private void Update()
        {
            UpdateState();
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Changes the current character state and triggers appropriate logic based on the new state.
        /// </summary>
        public void ChangeState(CharacterStateType newState)
        {
            if (CurrentState == CharacterStateType.Dead) return;

            CurrentState = newState;
            characterAnimator.SetState(newState);

            switch (newState)
            {
                case CharacterStateType.Patrol:
                    character.Mover.ResumeMoving();
                    character.Mover.UpdatePatrolMovement();
                    break;
                case CharacterStateType.Chase:
                    character.Mover.ResumeMoving();
                    character.Mover.StartChasingPlayer();
                    break;
                case CharacterStateType.Attack:
                    character.Mover.StopMoving();
                    attackHandler.StartAttack();
                    break;
                case CharacterStateType.Dead:
                    character.Health.Die();
                    break;
            }
        }

        /// <summary>
        /// Performs continuous updates while in specific states, such as updating chase target.
        /// </summary>
        private void UpdateState()
        {
            switch (CurrentState)
            {
                case CharacterStateType.Chase:
                    character.Mover.UpdateChaseTarget();
                    break;
            }
        }
    }
}

