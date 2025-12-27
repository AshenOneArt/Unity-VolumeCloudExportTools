using UnityEngine;

public class AdvancedCameraController : MonoBehaviour
{
    [Header("移动设置")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float shiftSpeedMultiplier = 2f;
    [SerializeField] private float scrollWheelSpeed = 10f;
    [SerializeField] private float scrollWheelSensitivity = 2f;
    
    [Header("鼠标视角控制")]
    [SerializeField] private bool enableMouseLook = true;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private bool invertY = false;
    [SerializeField] private bool requireMouseButton = false; // Tab切换：是否需要按住鼠标键控制视角
    [SerializeField] private KeyCode mouseLookKey = KeyCode.Mouse1; // 需要按住的鼠标键
    
    [Header("视角限制")]
    [SerializeField] private bool limitVerticalRotation = true;
    [SerializeField] private float minVerticalAngle = -90f;
    [SerializeField] private float maxVerticalAngle = 90f;
    
    [Header("平滑设置")]
    [SerializeField] private bool useSmoothMovement = true;
    [SerializeField] private float movementSmoothTime = 0.1f;
    [SerializeField] private bool useSmoothRotation = true;
    [SerializeField] private float rotationSmoothTime = 0.1f;
    
    [Header("限制设置")]
    [SerializeField] private bool enableHeightLimit = false;
    [SerializeField] private float minHeight = 0f;
    [SerializeField] private float maxHeight = 100f;
    
    [Header("输入设置")]
    [SerializeField] private KeyCode forwardKey = KeyCode.W;
    [SerializeField] private KeyCode backwardKey = KeyCode.S;
    [SerializeField] private KeyCode leftKey = KeyCode.A;
    [SerializeField] private KeyCode rightKey = KeyCode.D;
    [SerializeField] private KeyCode upKey = KeyCode.E;
    [SerializeField] private KeyCode downKey = KeyCode.Q;
    [SerializeField] private KeyCode speedUpKey = KeyCode.LeftShift;
    [SerializeField] private KeyCode toggleMouseLookKey = KeyCode.Tab; // 切换鼠标控制模式
    
    // 私有变量
    private Vector3 velocity = Vector3.zero;
    private Vector3 targetVelocity = Vector3.zero;
    private Vector3 smoothVelocity = Vector3.zero;
    
    private float rotationX = 0f;
    private float rotationY = 0f;
    private float targetRotationX = 0f;
    private float targetRotationY = 0f;
    private Vector2 rotationVelocity = Vector2.zero;
    
    private bool mouseLookActive = false;
    private bool wasMouseLookActive = false;
    
    void Start()
    {
        // 初始化旋转角度
        Vector3 currentRotation = transform.eulerAngles;
        rotationY = currentRotation.y;
        rotationX = currentRotation.x;
        
        // 处理角度范围
        if (rotationX > 180f)
            rotationX -= 360f;
            
        targetRotationX = rotationX;
        targetRotationY = rotationY;
        
        // 初始化光标状态
        UpdateCursorState();
    }
    
    void Update()
    {
        HandleMouseLookToggle();
        HandleMouseLook();
        HandleMovementInput();
        ApplyMovement();
        ApplyRotation();
    }
    
    private void HandleMouseLookToggle()
    {
        // Tab键切换控制模式
        if (Input.GetKeyDown(toggleMouseLookKey))
        {
            requireMouseButton = !requireMouseButton;
            Debug.Log($"鼠标控制模式: {(requireMouseButton ? "需要按住鼠标键" : "直接控制视角")}");
            UpdateCursorState();
        }
        
        // 检查鼠标控制激活状态
        bool shouldActivateMouseLook = false;
        
        if (requireMouseButton)
        {
            // 需要按住鼠标键的模式
            shouldActivateMouseLook = Input.GetKey(mouseLookKey);
        }
        else
        {
            // 直接控制模式
            shouldActivateMouseLook = true;
        }
        
        mouseLookActive = enableMouseLook && shouldActivateMouseLook;
        
        // 处理鼠标锁定状态变化
        if (mouseLookActive != wasMouseLookActive)
        {
            UpdateCursorState();
        }
        
        wasMouseLookActive = mouseLookActive;
    }
    
    private void UpdateCursorState()
    {
        if (!requireMouseButton && enableMouseLook)
        {
            // 直接控制模式：隐藏并锁定光标
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (requireMouseButton)
        {
            // 按键控制模式：根据鼠标键状态决定
            if (mouseLookActive)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
        else
        {
            // 禁用鼠标控制：显示光标
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
    
    private void HandleMouseLook()
    {
        if (!mouseLookActive) return;
        
        // 获取鼠标输入
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        
        if (invertY)
            mouseY = -mouseY;
        
        // 更新目标旋转角度
        targetRotationY += mouseX;
        targetRotationX -= mouseY;
        
        // 限制垂直旋转
        if (limitVerticalRotation)
        {
            targetRotationX = Mathf.Clamp(targetRotationX, minVerticalAngle, maxVerticalAngle);
        }
    }
    
    private void HandleMovementInput()
    {
        // 重置目标速度
        targetVelocity = Vector3.zero;
        
        // 获取输入
        Vector3 inputDirection = Vector3.zero;
        
        // WSAD 移动（相对于相机朝向）
        if (Input.GetKey(forwardKey))
            inputDirection += transform.forward;
        if (Input.GetKey(backwardKey))
            inputDirection -= transform.forward;
        if (Input.GetKey(rightKey))
            inputDirection += transform.right;
        if (Input.GetKey(leftKey))
            inputDirection -= transform.right;
        
        // QE 上下移动（世界坐标系）
        if (Input.GetKey(upKey))
            inputDirection += Vector3.up;
        if (Input.GetKey(downKey))
            inputDirection += Vector3.down;
        
        // 滚轮前进后退
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scrollInput) > 0.01f)
        {
            inputDirection += transform.forward * scrollInput * scrollWheelSpeed * scrollWheelSensitivity;
        }
        
        // 归一化输入方向
        if (inputDirection.magnitude > 1f)
            inputDirection = inputDirection.normalized;
        
        // 计算移动速度
        float currentSpeed = moveSpeed;
        
        // Shift 加速
        if (Input.GetKey(speedUpKey))
            currentSpeed *= shiftSpeedMultiplier;
        
        // 设置目标速度
        targetVelocity = inputDirection * currentSpeed;
    }
    
    private void ApplyMovement()
    {
        // 平滑移动或直接移动
        if (useSmoothMovement)
        {
            velocity = Vector3.SmoothDamp(velocity, targetVelocity, ref smoothVelocity, movementSmoothTime);
        }
        else
        {
            velocity = targetVelocity;
        }
        
        // 计算新位置
        Vector3 newPosition = transform.position + velocity * Time.deltaTime;
        
        // 高度限制
        if (enableHeightLimit)
        {
            newPosition.y = Mathf.Clamp(newPosition.y, minHeight, maxHeight);
        }
        
        // 应用新位置
        transform.position = newPosition;
    }
    
    private void ApplyRotation()
    {
        if (!mouseLookActive) return;
        
        // 平滑旋转或直接旋转
        if (useSmoothRotation)
        {
            rotationX = Mathf.SmoothDampAngle(rotationX, targetRotationX, ref rotationVelocity.x, rotationSmoothTime);
            rotationY = Mathf.SmoothDampAngle(rotationY, targetRotationY, ref rotationVelocity.y, rotationSmoothTime);
        }
        else
        {
            rotationX = targetRotationX;
            rotationY = targetRotationY;
        }
        
        // 应用旋转
        transform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);
    }
    
    // 公共方法
    public void SetMoveSpeed(float speed)
    {
        moveSpeed = speed;
    }
    
    public void SetMouseSensitivity(float sensitivity)
    {
        mouseSensitivity = sensitivity;
    }
    
    public void SetMovementEnabled(bool enabled)
    {
        this.enabled = enabled;
        if (!enabled)
        {
            velocity = Vector3.zero;
            targetVelocity = Vector3.zero;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            UpdateCursorState();
        }
    }
    
    public void TeleportTo(Vector3 position, Vector3? rotation = null)
    {
        transform.position = position;
        velocity = Vector3.zero;
        targetVelocity = Vector3.zero;
        
        if (rotation.HasValue)
        {
            Vector3 rot = rotation.Value;
            rotationX = rot.x;
            rotationY = rot.y;
            targetRotationX = rot.x;
            targetRotationY = rot.y;
            transform.rotation = Quaternion.Euler(rot);
        }
    }
    
    public void ResetRotation()
    {
        rotationX = 0f;
        rotationY = 0f;
        targetRotationX = 0f;
        targetRotationY = 0f;
        transform.rotation = Quaternion.identity;
    }
    
    // 调试信息
    void OnGUI()
    {
        if (!Application.isPlaying || !enabled) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 350, 250));
        GUILayout.Label("高级相机控制器:");
        GUILayout.Label($"WSAD: 前后左右移动");
        GUILayout.Label($"QE: 上下移动");
        GUILayout.Label($"Shift: 加速 ({shiftSpeedMultiplier}x)");
        GUILayout.Label($"滚轮: 快速前进后退");
        GUILayout.Label($"Tab: 切换鼠标控制模式");
        
        if (requireMouseButton)
            GUILayout.Label($"{mouseLookKey}: 按住启用鼠标视角");
        else
            GUILayout.Label("鼠标: 直接控制视角 (光标隐藏)");
            
        GUILayout.Label($"");
        GUILayout.Label($"控制模式: {(requireMouseButton ? "按键模式" : "直接模式")}");
        GUILayout.Label($"鼠标控制: {(mouseLookActive ? "激活" : "未激活")}");
        GUILayout.Label($"光标状态: {(Cursor.visible ? "显示" : "隐藏")}");
        GUILayout.Label($"当前速度: {velocity.magnitude:F2}");
        GUILayout.Label($"位置: ({transform.position.x:F1}, {transform.position.y:F1}, {transform.position.z:F1})");
        GUILayout.Label($"旋转: ({rotationX:F1}°, {rotationY:F1}°)");
        GUILayout.EndArea();
    }
    
    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            // 失去焦点时总是显示光标
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            // 重新获得焦点时恢复光标状态
            UpdateCursorState();
        }
    }
}
