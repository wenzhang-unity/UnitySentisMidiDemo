using System;
using System.Collections;
using System.Collections.Generic;
using MidiPlayerTK;
using UnityEngine;
using Random = UnityEngine.Random;

[RequireComponent(typeof(Renderer))]
public class Visualizer : MonoBehaviour
{
    [SerializeField]
    int m_Channel;
    
    [SerializeField]
    float m_DecayRatePerSecond = 0.9f;

    [SerializeField]
    float m_MaxHeightDelta = 5f;

    [SerializeField]
    int m_LowestNote = 40;
    
    Material m_Material;
    Light m_Light;
    
    float m_LightIntensity;
    static readonly int k_EmissionColor = Shader.PropertyToID("_EmissionColor");

    Color m_StartColor;
    
    Vector3 m_StartPosition;
    
    Vector3 m_TopPosition;

    Renderer m_Renderer;

    // Start is called before the first frame update
    void Start()
    {
        // MidiGen.OnNotePlayed += React;
        m_Material = GetComponent<Renderer>().material;
        m_StartColor = m_Material.GetColor(k_EmissionColor);
        
        m_Light = GetComponent<Light>();
        m_LightIntensity = m_Light.intensity;
        m_Light.color = m_StartColor;
        
        m_StartPosition = transform.position;
        m_TopPosition = m_StartPosition + Vector3.up * m_MaxHeightDelta;
        
        m_Renderer = GetComponent<Renderer>();
        m_Renderer.enabled = false;
        m_Light.enabled = false;
    }

    // Update is called once per frame
    void Update()
    {
        var decay = (1f - m_DecayRatePerSecond * Time.deltaTime);
        
        // Decay intensity of emission
        var emission = m_Material.GetColor(k_EmissionColor);
        emission *= decay;
        m_Material.SetColor(k_EmissionColor, emission);
        
        // Decay intensity of light
        m_Light.intensity *= decay;
        
        var position = Vector3.Lerp(transform.position, m_StartPosition, (1 - decay) * 0.1f);
        transform.position = position;
        
        if (Vector3.Distance(position, m_StartPosition) < 0.001f)
        {
            m_Renderer.enabled = false;
            m_Light.enabled = false;
        }
    }

    // void OnGUI()
    // {
    //     if (GUI.Button(new Rect(10, 10, 150, 100), "Hello World!"))
    //     {
    //         React(new MPTKEvent()
    //         {
    //             Channel = m_Channel,
    //             Value = Random.Range(0, 127)
    //         });
    //     }
    // }

    void React(MPTKEvent ev)
    {
        if (ev.Channel != m_Channel)
        {
            return;
        }
        
        m_Material.SetColor(k_EmissionColor, m_StartColor);
        m_Light.intensity = m_LightIntensity;
        
        float noteRange = 127 - m_LowestNote;
        var lerpValue = Mathf.Max(0, ev.Value - m_LowestNote) / noteRange;
        var targetPosition = Vector3.Lerp(m_StartPosition, m_TopPosition, lerpValue);
        
        var position = Vector3.Lerp(transform.position, targetPosition, 0.01f);
        transform.position = position;
        
        m_Renderer.enabled = true;
        m_Light.enabled = true;
    }
}
