using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    [SerializeField]
    float m_Speed = 10f;
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        // Orbit around the origin
        transform.RotateAround(Vector3.zero, Vector3.up, m_Speed * Time.deltaTime);
    }
}
