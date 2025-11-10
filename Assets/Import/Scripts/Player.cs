using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody2D))]
public class Player : MonoBehaviour
{
    private Rigidbody2D rb;
    private Animator animator;
    private bool isGrounded;
    private Vector2 startPosition;
    private bool isReturning; // buat mencegah coroutine dobel

    public float jumpForce = 14f;
    public float gravityScale = 3.5f;
    public float forwardForce = 3f;
    public float returnDelay = 0.2f; // jeda sebelum balik
    public float returnSpeed = 5f;

    public Transform groundCheck;
    public float groundCheckRadius = 0.1f;
    public LayerMask groundLayer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        rb.freezeRotation = true;
    }

    private void Start()
    {
        startPosition = transform.position;
    }
    
    private void OnEnable()
    {
        rb.velocity = Vector2.zero;
        isReturning = false;
    }

    private void Update()
    {
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        if (isGrounded && Input.GetButtonDown("Jump") && !isReturning)
        {
            rb.velocity = new Vector2(forwardForce, jumpForce);
            StartCoroutine(ReturnInAir());
        }

        animator.SetBool("isGrounded", isGrounded);
    }

    private void FixedUpdate()
    {
        if (rb.velocity.y < 0)
            rb.velocity += Vector2.up * Physics2D.gravity.y * (gravityScale - 1) * Time.fixedDeltaTime;
    }

    private IEnumerator ReturnInAir()
    {
        isReturning = true;

        // Tunggu sedikit waktu biar sempat maju dulu
        yield return new WaitForSeconds(returnDelay);

        // Balik ke posisi awal selama di udara
        while (Vector2.Distance(transform.position, startPosition) > 0.05f)
        {
            transform.position = Vector2.MoveTowards(transform.position, startPosition, returnSpeed * Time.deltaTime);
            yield return null;
        }

        isReturning = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Obstacle"))
            GameManager.Instance.GameOver();
    }
}
