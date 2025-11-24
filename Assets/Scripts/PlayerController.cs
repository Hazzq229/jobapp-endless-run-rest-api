using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class PlayerController : MonoBehaviour
{
    private Animator animator;
    private Collider2D playerCollider;
    private bool isJumping = false;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        playerCollider = GetComponent<Collider2D>();
        
        GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
    }

    private void Update()
    {
        if (Input.GetButtonDown("Jump") && !isJumping)
        {
            PerformJump();
        }
    }

    private void PerformJump()
    {
        isJumping = true;
        animator.SetTrigger("isJumping");
    }

    public void AnimEvent_DisableCollider()
    {
        playerCollider.enabled = false;
    }
    public void AnimEvent_EnableCollider()
    {
        playerCollider.enabled = true;
        isJumping = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Obstacle"))
        {
            GameManager.Instance.GameOver();
        }
    }
}