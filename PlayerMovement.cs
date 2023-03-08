// Some stupid rigidbody based movement by Dani

using System;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{

    [Header("Assignables")]
    [Tooltip("this is a reference to the MainCamera object, not the parent of it.")]
    public Transform playerCam;
    [Tooltip("reference to orientation object, needed for moving forward and not up or something.")]
    public Transform orientation;
    [Tooltip("LayerMask for ground layer, important because otherwise the collision detection wont know what ground is")]
    public LayerMask whatIsGround;
    private Rigidbody rb;

    [Header("Rotation and look")]
    private float xRotation;
    [Tooltip("mouse/look sensitivity")]
    public float sensitivity = 50f;
    private float sensMultiplier = 1.5f;

    [Header("Movement")]
    [Tooltip("additive force amount. every physics update that forward is pressed, this force (multiplied by 1/tickrate) will be added to the player.")]
    public float moveSpeed = 4500;
    [Tooltip("maximum local velocity before input is cancelled")]
    public float maxSpeed = 20;
    [Tooltip("normal countermovement when not crouching.")]
    public float counterMovement = 0.175f;
    private float threshold = 0.01f;
    [Tooltip("the maximum angle the ground can have relative to the players up direction.")]
    public float maxSlopeAngle = 35f;
    private Vector3 crouchScale = new Vector3(1, 0.5f, 1);
    private Vector3 playerScale;
    [Tooltip("forward force for when a crouch is started.")]
    public float slideForce = 400;
    [Tooltip("countermovement when sliding. this doesnt work the same way as normal countermovement.")]
    public float slideCounterMovement = 0.2f;
    private bool readyToJump = true;
    private float jumpCooldown = 0.25f;
    [Tooltip("this determines the jump force but is also applied when jumping off of walls, if you decrease it, you may end up being able to walljump and then get back onto the wall leading to infinite height.")]
    public float jumpForce = 550f; 
    float x, y;
    bool jumping;
    private Vector3 normalVector = Vector3.up;

    [Header("Wallrunning")]
    private float actualWallRotation;
    private float wallRotationVel;
    private Vector3 wallNormalVector;
    [Tooltip("when wallrunning, an upwards force is constantly applied to negate gravity by about half (at default), increasing this value will lead to more upwards force and decreasing will lead to less upwards force.")]
    public float wallRunGravity = 1;
    [Tooltip("when a wallrun is started, an upwards force is applied, this describes that force.")]
    public float initialForce = 20f; 
    [Tooltip("float to choose how much force is applied outwards when ending a wallrun. this should always be greater than Jump Force")]
    public float escapeForce = 600f;
    private float wallRunRotation;
    [Tooltip("how much you want to rotate the camera sideways while wallrunning")]
    public float wallRunRotateAmount = 10f;
    [Tooltip("a bool to check if the player is wallrunning because thats kinda necessary.")]
    public bool isWallRunning;
    [Tooltip("a bool to determine whether or not to actually allow wallrunning.")]
    public bool useWallrunning = true;

    [Header("Collisions")]
    [Tooltip("a bool to check if the player is on the ground.")]
    public bool grounded;
    [Tooltip("a bool to check if the player is currently crouching.")]
    public bool crouching;
    private bool surfing;
    private bool cancellingGrounded;
    private bool cancellingSurf;
    private bool cancellingWall;
    private bool onWall;
    private bool cancelling;

    public static PlayerMovement Instance { get; private set; }

    void Awake()
    {

        Instance = this;

        rb = GetComponent<Rigidbody>();
        
        //Create a physic material with no friction to allow for wallrunning and smooth movement not being dependant
        //and smooth movement not being dependant on the in-built unity physics engine, apart from collisions.
        PhysicMaterial mat = new PhysicMaterial("tempMat");

        mat.bounceCombine = PhysicMaterialCombine.Average;

        mat.bounciness = 0;

        mat.frictionCombine = PhysicMaterialCombine.Minimum;

        mat.staticFriction = 0;
        mat.dynamicFriction = 0;

        gameObject.GetComponent<Collider>().material = mat;
    }

    void Start()
    {
        playerScale = transform.localScale;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        readyToJump = true;
        wallNormalVector = Vector3.up;
    }


    private void FixedUpdate()
    {
        Movement();
    }

    private void Update()
    {
        MyInput();
        Look();
    }

    private void LateUpdate()
    {
        //call the wallrunning Function
        WallRunning();
        WallRunRotate();
    }

    private void WallRunRotate()
    {
        FindWallRunRotation();
        float num = 12f;
        actualWallRotation = Mathf.SmoothDamp(actualWallRotation, wallRunRotation, ref wallRotationVel, num * Time.deltaTime);
        playerCam.localRotation = Quaternion.Euler(playerCam.rotation.eulerAngles.x, playerCam.rotation.eulerAngles.y, actualWallRotation);
    }

    /// <summary>
    /// Find user input. Should put this in its own class but im lazy
    /// </summary>
    private void MyInput()
    {
        x = Input.GetAxisRaw("Horizontal");
        y = Input.GetAxisRaw("Vertical");
        jumping = Input.GetButton("Jump");
        crouching = Input.GetKey(KeyCode.LeftControl);

        //Crouching
        if (Input.GetKeyDown(KeyCode.LeftControl))
            StartCrouch();
        if (Input.GetKeyUp(KeyCode.LeftControl))
            StopCrouch();
    }

    private void StartCrouch()
    {
        transform.localScale = crouchScale;
        transform.position = new Vector3(transform.position.x, transform.position.y - 0.5f, transform.position.z);
        if (rb.velocity.magnitude > 0.2f && grounded)
        {
            if (grounded)
            {
                rb.AddForce(orientation.transform.forward * slideForce);
            }
        }
    }

    private void StopCrouch()
    {
        transform.localScale = playerScale;
        transform.position = new Vector3(transform.position.x, transform.position.y + 0.5f, transform.position.z);
    }

    private void Movement()
    {
        //Extra gravity
        rb.AddForce(Vector3.down * Time.deltaTime * 10);

        //Find actual velocity relative to where player is looking
        Vector2 mag = FindVelRelativeToLook();
        float xMag = mag.x, yMag = mag.y;

        //Counteract sliding and sloppy movement
        CounterMovement(x, y, mag);

        //If holding jump && ready to jump, then jump
        if (readyToJump && jumping) Jump();

        //Set max speed
        float maxSpeed = this.maxSpeed;

        //If sliding down a ramp, add force down so player stays grounded and also builds speed
        if (crouching && grounded && readyToJump)
        {
            rb.AddForce(Vector3.down * Time.deltaTime * 3000);
            return;
        }

        //If speed is larger than maxspeed, cancel out the input so you don't go over max speed
        if (x > 0 && xMag > maxSpeed) x = 0;
        if (x < 0 && xMag < -maxSpeed) x = 0;
        if (y > 0 && yMag > maxSpeed) y = 0;
        if (y < 0 && yMag < -maxSpeed) y = 0;

        //Some multipliers
        float multiplier = 1f, multiplierV = 1f;

        // Movement in air
        if (!grounded)
        {
            multiplier = 0.5f;
            multiplierV = 0.5f;
        }

        // Movement while sliding
        if (grounded && crouching) multiplierV = 0f;

        //Apply forces to move player
        rb.AddForce(orientation.transform.forward * y * moveSpeed * Time.deltaTime * multiplier * multiplierV);
        rb.AddForce(orientation.transform.right * x * moveSpeed * Time.deltaTime * multiplier);
    }

    private void Jump()
    {
        if ((grounded || isWallRunning || surfing) && readyToJump)
        {
            MonoBehaviour.print("jumping");
            Vector3 velocity = rb.velocity;
            readyToJump = false;
            rb.AddForce(Vector2.up * jumpForce * 1.5f);
            rb.AddForce(normalVector * jumpForce * 0.5f);
            if (rb.velocity.y < 0.5f)
            {
                rb.velocity = new Vector3(velocity.x, 0f, velocity.z);
            }
            else if (rb.velocity.y > 0f)
            {
                rb.velocity = new Vector3(velocity.x, velocity.y / 2f, velocity.z);
            }
            if (isWallRunning)
            {
                rb.AddForce(wallNormalVector * jumpForce * 3f);
            }
            Invoke("ResetJump", jumpCooldown);
            if (isWallRunning)
            {
                isWallRunning = false;
            }
        }
    }

    private void ResetJump()
    {
        readyToJump = true;
    }

    private float desiredX;
    private void Look()
    {
        float mouseX = Input.GetAxis("Mouse X") * sensitivity * Time.fixedDeltaTime * sensMultiplier;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity * Time.fixedDeltaTime * sensMultiplier;

        //Find current look rotation
        Vector3 rot = playerCam.transform.localRotation.eulerAngles;
        desiredX = rot.y + mouseX;

        //Rotate, and also make sure we dont over- or under-rotate.
        xRotation -= mouseY;
        float clamp = 89.5f;
        xRotation = Mathf.Clamp(xRotation, -clamp, clamp);

        //Perform the rotations
        playerCam.transform.localRotation = Quaternion.Euler(xRotation, desiredX, 0);
        orientation.transform.localRotation = Quaternion.Euler(0, desiredX, 0);
    }

    private void CounterMovement(float x, float y, Vector2 mag)
    {
        if (!grounded || jumping) return;

        //Slow down sliding
        if (crouching)
        {
            rb.AddForce(moveSpeed * Time.deltaTime * -rb.velocity.normalized * slideCounterMovement);
            return;
        }

        //Counter movement
        if (Math.Abs(mag.x) > threshold && Math.Abs(x) < 0.05f || (mag.x < -threshold && x > 0) || (mag.x > threshold && x < 0))
        {
            rb.AddForce(moveSpeed * orientation.transform.right * Time.deltaTime * -mag.x * counterMovement);
        }
        if (Math.Abs(mag.y) > threshold && Math.Abs(y) < 0.05f || (mag.y < -threshold && y > 0) || (mag.y > threshold && y < 0))
        {
            rb.AddForce(moveSpeed * orientation.transform.forward * Time.deltaTime * -mag.y * counterMovement);
        }

        //Limit diagonal running. This will also cause a full stop if sliding fast and un-crouching, so not optimal.
        if (Mathf.Sqrt((Mathf.Pow(rb.velocity.x, 2) + Mathf.Pow(rb.velocity.z, 2))) > maxSpeed)
        {
            float fallspeed = rb.velocity.y;
            Vector3 n = rb.velocity.normalized * maxSpeed;
            rb.velocity = new Vector3(n.x, fallspeed, n.z);
        }
    }

    /// <summary>
    /// Find the velocity relative to where the player is looking
    /// Useful for vectors calculations regarding movement and limiting movement
    /// </summary>
    /// <returns></returns>
    public Vector2 FindVelRelativeToLook()
    {
        float lookAngle = orientation.transform.eulerAngles.y;
        float moveAngle = Mathf.Atan2(rb.velocity.x, rb.velocity.z) * Mathf.Rad2Deg;

        float u = Mathf.DeltaAngle(lookAngle, moveAngle);
        float v = 90 - u;

        float magnitue = rb.velocity.magnitude;
        float yMag = magnitue * Mathf.Cos(u * Mathf.Deg2Rad);
        float xMag = magnitue * Mathf.Cos(v * Mathf.Deg2Rad);

        return new Vector2(xMag, yMag);
    }
    //a lot of math (dont touch)
    private void FindWallRunRotation()
    {

        if (!isWallRunning)
        {
            wallRunRotation = 0f;
            return;
        }
        _ = new Vector3(0f, playerCam.transform.rotation.y, 0f).normalized;
        new Vector3(0f, 0f, 1f);
        float num = 0f;
        float current = playerCam.transform.rotation.eulerAngles.y;
        if (Math.Abs(wallNormalVector.x - 1f) < 0.1f)
        {
            num = 90f;
        }
        else if (Math.Abs(wallNormalVector.x - -1f) < 0.1f)
        {
            num = 270f;
        }
        else if (Math.Abs(wallNormalVector.z - 1f) < 0.1f)
        {
            num = 0f;
        }
        else if (Math.Abs(wallNormalVector.z - -1f) < 0.1f)
        {
            num = 180f;
        }
        num = Vector3.SignedAngle(new Vector3(0f, 0f, 1f), wallNormalVector, Vector3.up);
        float num2 = Mathf.DeltaAngle(current, num);
        wallRunRotation = (0f - num2 / 90f) * wallRunRotateAmount;
        if (!useWallrunning)
        {
            return;
        }
        if ((Mathf.Abs(wallRunRotation) < 4f && y > 0f && Math.Abs(x) < 0.1f) || (Mathf.Abs(wallRunRotation) > 22f && y < 0f && Math.Abs(x) < 0.1f))
        {
            if (!cancelling)
            {
                cancelling = true;
                CancelInvoke("CancelWallrun");
                Invoke("CancelWallrun", 0.2f);
            }
        }
        else
        {
            cancelling = false;
            CancelInvoke("CancelWallrun");
        }
    }

    private bool IsFloor(Vector3 v)
    {
        return Vector3.Angle(Vector3.up, v) < maxSlopeAngle;
    }

    private bool IsSurf(Vector3 v)
    {
        float num = Vector3.Angle(Vector3.up, v);
        if (num < 89f)
        {
            return num > maxSlopeAngle;
        }
        return false;
    }

    private bool IsWall(Vector3 v)
    {
        return Math.Abs(90f - Vector3.Angle(Vector3.up, v)) < 0.05f;
    }

    private bool IsRoof(Vector3 v)
    {
        return v.y == -1f;
    }

    /// <summary>
    /// Handle ground detection
    /// </summary>
    private void OnCollisionStay(Collision other)
    {
        int layer = other.gameObject.layer;
        if ((int)whatIsGround != ((int)whatIsGround | (1 << layer)))
        {
            return;
        }
        for (int i = 0; i < other.contactCount; i++)
        {
            Vector3 normal = other.contacts[i].normal;
            if (IsFloor(normal))
            {
                if (isWallRunning)
                {
                    isWallRunning = false;
                }
                grounded = true;
                normalVector = normal;
                cancellingGrounded = false;
                CancelInvoke("StopGrounded");
            }
            if (IsWall(normal) && (layer == (int)whatIsGround || (int)whatIsGround == -1 || layer == LayerMask.NameToLayer("Ground") || layer == LayerMask.NameToLayer("ground"))) //seriously what is this
            {
                StartWallRun(normal);
                onWall = true;
                cancellingWall = false;
                CancelInvoke("StopWall");
            }
            if (IsSurf(normal))
            {
                surfing = true;
                cancellingSurf = false;
                CancelInvoke("StopSurf");
            }
            IsRoof(normal);
        }
        float num = 3f;
        if (!cancellingGrounded)
        {
            cancellingGrounded = true;
            Invoke("StopGrounded", Time.deltaTime * num);
        }
        if (!cancellingWall)
        {
            cancellingWall = true;
            Invoke("StopWall", Time.deltaTime * num);
        }
        if (!cancellingSurf)
        {
            cancellingSurf = true;
            Invoke("StopSurf", Time.deltaTime * num);
        }
    }

    private void StopGrounded()
    {
        grounded = false;
    }

    private void StopWall()
    {
        onWall = false;
        isWallRunning = false;
    }

    private void StopSurf()
    {
        surfing = false;
    }

    //wallrunning functions
    private void CancelWallrun()
    {
        //for when we want to stop wallrunning
        MonoBehaviour.print("cancelled wallrun");
        Invoke("GetReadyToWallrun", 0.1f);
        rb.AddForce(wallNormalVector * escapeForce);
        isWallRunning = false;
    }

    private void StartWallRun(Vector3 normal)
    {
        MonoBehaviour.print("wallrunning");
        //cancels all y momentum and then applies an upwards force.
        if (!grounded && useWallrunning)
        {
            wallNormalVector = normal;
            if (!isWallRunning)
            {
                rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
                rb.AddForce(Vector3.up * initialForce, ForceMode.Impulse);
            }
            isWallRunning = true;
        }
    }

    private void WallRunning()
    {
        //checks if the wallrunning bool is set to true and if it is then applies
        //a force to counter gravity enough to make it feel like wallrunning
        if (isWallRunning)
        {
            rb.AddForce(-wallNormalVector * Time.deltaTime * moveSpeed);
            rb.AddForce(Vector3.up * Time.deltaTime * rb.mass * wallRunGravity * -Physics.gravity.y * 0.4f);
        }
    }

}
