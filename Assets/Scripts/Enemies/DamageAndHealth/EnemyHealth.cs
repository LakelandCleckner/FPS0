using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    public float maxHealth = 100f;
    private float currentHealth;

    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage, BodyPart partHit)
    {
        currentHealth -= damage;

        Debug.Log("Hit " + partHit + " for " + damage);
        Debug.Log("Remaining Health: " + currentHealth);

        switch (partHit)
        {
            case BodyPart.Head:
                //crit logic
                break;

            case BodyPart.Leg:
                //slow logic
                break;

            case BodyPart.Arm:
                // accuracy/fire rate logic
                break;
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        Debug.Log("Enemy Died");
        Destroy(gameObject);
    }
}