using System.Collections;
using System.Collections.Generic;
using MidiPlayerTK;
using UnityEngine;

public class PianoRoll : MonoBehaviour
{
    [SerializeField]
    Material m_BackgroundMaterial;
    
    [SerializeField]
    Material m_NoteMaterial;
    
    readonly List<MPTKEvent> m_Notes = new List<MPTKEvent>();
    
    // Number of seconds spanned by the piano roll
    float m_TimeScale = 1f;
    
    // 16 legible colours
    static readonly Color[] m_NoteColors = new[]
    {
        new Color(1.0f, 0.341f, 0.2f),  // #FF5733 - Bright Orange
        new Color(1.0f, 0.702f, 0.0f),  // #FFB300 - Golden Yellow
        new Color(0.0f, 0.502f, 0.0f),  // #008000 - Green
        new Color(0.118f, 0.565f, 1.0f),// #1E90FF - Dodger Blue
        new Color(0.294f, 0.0f, 0.510f),// #4B0082 - Indigo
        new Color(0.502f, 0.0f, 0.502f),// #800080 - Purple
        new Color(1.0f, 0.082f, 0.576f),// #FF1493 - Deep Pink
        new Color(0.545f, 0.271f, 0.075f),// #8B4513 - Saddle Brown
        new Color(0.0f, 0.808f, 0.82f), // #00CED1 - Dark Turquoise
        new Color(1.0f, 0.843f, 0.0f),  // #FFD700 - Gold
        new Color(0.863f, 0.078f, 0.235f),// #DC143C - Crimson
        new Color(0.580f, 0.0f, 0.827f),// #9400D3 - Dark Violet
        new Color(1.0f, 0.271f, 0.0f),  // #FF4500 - Orange Red
        new Color(0.275f, 0.514f, 0.71f),// #4682B4 - Steel Blue
        new Color(0.855f, 0.647f, 0.125f),// #DAA520 - Goldenrod
        new Color(0.18f, 0.545f, 0.341f) // #2E8B57 - Sea Green
    };
    
    // Start is called before the first frame update
    void Start()
    {
        // Create a plane facing the camera
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.transform.parent = transform;
        plane.transform.localPosition = new Vector3(0, 0, 1);
        plane.transform.localRotation = Quaternion.Euler(-90, 0, 0);
        plane.transform.localScale = new Vector3(1, 0.5f, 1);
        
        plane.GetComponent<MeshRenderer>().material = m_BackgroundMaterial;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    
    public void Clear()
    {
        m_Notes.Clear();
    }
    
    public void Add(MPTKEvent note)
    {
        m_Notes.Add(note);
    }
}
