using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class Player : MonoBehaviour
{
    private CharacterController character;
    private Vector3 direction;

    public float jumpForce = 8f;
    public float gravity = 9.81f * 2f;

    private void Awake()
    {
        character = GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        direction = Vector3.zero;
    }

    private void Update()
    {
        if (character.isGrounded)
        {
            // jaga tetap di tanah, jangan terus dorong ke bawah
            direction.y = -1f;

            if (Input.GetButton("Jump"))
            {
                direction.y = jumpForce;
            }
        }
        else
        {
            // baru tambahkan gravitasi ketika di udara
            direction.y -= gravity * Time.deltaTime;
        }

        character.Move(direction * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Obstacle")) {
            GameManager.Instance.GameOver();
        }
    }

}
