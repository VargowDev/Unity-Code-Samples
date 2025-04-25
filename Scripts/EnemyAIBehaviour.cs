using System.Collections;
using UnityEngine;

namespace TheRevenantEngine.Characters
{
    /// <summary>
    /// Handles the core enemy AI behavior logic, including detection, chasing, and attacking the player based on distance.
    /// </summary>
    public class EnemyAIBehaviour : MonoBehaviour
    {
        [Header("Detection Settings")]
        [SerializeField] private float detectionRange = 10f;
        [SerializeField] private float attackRange = 1.5f;
        [SerializeField] private float attackCooldown = 1.5f;

        private Character character;
        private Transform playerTransform;
        private bool isAttacking = false;

        private void Awake()
        {
            character = GetComponent<Character>();
            playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
        }
        private void Start()
        {
            StartCoroutine(EnemyBehaviourLoop());
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Main behavior loop coroutine that checks distance to the player and transitions the enemy to appropriate states.
        /// </summary>
        private IEnumerator EnemyBehaviourLoop()
        {
            while (character.State.CurrentState != CharacterStateType.Dead)
            {
                if (playerTransform == null)
                {
                    character.State.ChangeState(CharacterStateType.Idle);
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

                if (distanceToPlayer > detectionRange)
                {
                    character.State.ChangeState(CharacterStateType.Patrol);
                }
                else if (distanceToPlayer > attackRange)
                {
                    character.State.ChangeState(CharacterStateType.Chase);
                }
                else if (!isAttacking)
                {
                    StartCoroutine(PerformAttack());
                }

                yield return new WaitForSeconds(0.2f);
            }
        }

        /// <summary>
        /// Handles enemy attack logic and enforces attack cooldowns.
        /// </summary>
        private IEnumerator PerformAttack()
        {
            isAttacking = true;
            character.State.ChangeState(CharacterStateType.Attack);

            yield return new WaitForSeconds(attackCooldown);

            isAttacking = false;
        }
    }
}
