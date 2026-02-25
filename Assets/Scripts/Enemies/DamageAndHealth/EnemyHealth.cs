using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    public float maxHealth = 100f;
    private float currentHealth;
    public float CurrentHealth
    {
        get { return currentHealth; }
    }

    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage, BodyPart partHit)
    {
        currentHealth = Mathf.Clamp(currentHealth - damage, 0f, maxHealth);

        Debug.Log("Hit " + partHit + " for " + damage);
        Debug.Log("Remaining Health: " + currentHealth);


        //Use later for effects (leg shot slow, etc)
        switch (partHit)
        {
            case BodyPart.Head:
                break;

            case BodyPart.Leg:
                break;

            case BodyPart.Arm:
                break;
        }

        //call Die() if health reaches 0
        if (currentHealth == 0f)
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