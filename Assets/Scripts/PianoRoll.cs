using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public class PianoRoll : MonoBehaviour
{
    [SerializeField]
    long m_WindowSize = 10_000;

    [SerializeField]
    Transform m_BackgroundPrefab;
    
    [SerializeField]
    Transform m_NotePrefab;

    Material m_NoteMaterial;

    ObjectPool<Transform> m_NoteObjectPool;

    readonly Dictionary<Transform, MeshRenderer> m_NoteRenderers = new();

    public readonly struct Note
    {
        public readonly int Channel;
        public readonly int Value;
        public readonly long Start;
        public readonly long Duration;

        public Note(int channel, int value, long start, long duration)
        {
            Channel = channel;
            Value = value;
            Start = start;
            Duration = duration;
        }
    }

    readonly List<Note> m_Notes = new List<Note>();
    readonly Stack<Transform> m_ActiveNotes = new Stack<Transform>();

    MeshFilter m_PlaneMeshFilter;

    long m_PlayHead;
    long m_WindowStart;

    // 16 legible colours
    static readonly Color[] k_NoteColors = new[]
    {
        new Color(1.0f, 0.341f, 0.2f), // #FF5733 - Bright Orange
        new Color(1.0f, 0.702f, 0.0f), // #FFB300 - Golden Yellow
        new Color(0.0f, 0.502f, 0.0f), // #008000 - Green
        new Color(0.118f, 0.565f, 1.0f), // #1E90FF - Dodger Blue
        new Color(0.294f, 0.0f, 0.510f), // #4B0082 - Indigo
        new Color(0.502f, 0.0f, 0.502f), // #800080 - Purple
        new Color(1.0f, 0.082f, 0.576f), // #FF1493 - Deep Pink
        new Color(0.545f, 0.271f, 0.075f), // #8B4513 - Saddle Brown
        new Color(0.0f, 0.808f, 0.82f), // #00CED1 - Dark Turquoise
        new Color(1.0f, 0.843f, 0.0f), // #FFD700 - Gold
        new Color(0.863f, 0.078f, 0.235f), // #DC143C - Crimson
        new Color(0.580f, 0.0f, 0.827f), // #9400D3 - Dark Violet
        new Color(1.0f, 0.271f, 0.0f), // #FF4500 - Orange Red
        new Color(0.275f, 0.514f, 0.71f), // #4682B4 - Steel Blue
        new Color(0.855f, 0.647f, 0.125f), // #DAA520 - Goldenrod
        new Color(0.18f, 0.545f, 0.341f) // #2E8B57 - Sea Green
    };

    MaterialPropertyBlock[] m_NotePropertyBlocks;
    static readonly int k_Color = Shader.PropertyToID("_BaseColor");

    // Start is called before the first frame update
    void Start()
    {
        var plane = Instantiate(m_BackgroundPrefab, transform);
        m_PlaneMeshFilter = plane.GetComponent<MeshFilter>();

        m_NoteObjectPool = new ObjectPool<Transform>(
            () =>
            {
                var noteTransform = Instantiate(m_NotePrefab, plane);
                m_NoteRenderers[noteTransform] = noteTransform.GetComponentInChildren<MeshRenderer>();
                return noteTransform;
            },
            note => note.gameObject.SetActive(true),
            note => note.gameObject.SetActive(false));

        m_NotePropertyBlocks = new MaterialPropertyBlock[k_NoteColors.Length];
        for (var i = 0; i < k_NoteColors.Length; i++)
        {
            m_NotePropertyBlocks[i] = new MaterialPropertyBlock();
            m_NotePropertyBlocks[i].SetColor(k_Color, k_NoteColors[i]);
        }
    }

    // Update is called once per frame
    void Update()
    {
        var bounds = m_PlaneMeshFilter.mesh.bounds;
        while (m_ActiveNotes.Count > 0)
        {
            m_NoteObjectPool.Release(m_ActiveNotes.Pop());
        }
        
        foreach (var note in m_Notes)
        {
            DrawNote(note, bounds);
        }
    }

    public void Clear()
    {
        m_Notes.Clear();
        m_WindowStart = 0;
    }

    public void Add(Note note)
    {
        m_Notes.Add(note);
        while (note.Start + note.Duration > m_WindowStart + m_WindowSize)
        {
            m_WindowStart += m_WindowSize / 2;
        }
    }

    void DrawNote(in Note note, Bounds bounds)
    {
        var start = note.Start;
        var end = note.Start + note.Duration;

        if (end < m_WindowStart || start > m_WindowStart + m_WindowSize)
        {
            return;
        }

        var x = Mathf.Clamp((start - m_WindowStart) / (float)m_WindowSize, 0, 1);
        var width = Mathf.Clamp((note.Duration) / (float)m_WindowSize, 0, 1);

        var y = note.Value / 127f;
        const float height = 1 / 127f;

        var noteTransform = m_NoteObjectPool.Get();
        noteTransform.localPosition = new Vector3(
            bounds.min.x + x * bounds.size.x,
            0,
            bounds.min.z + y * bounds.size.z
        );
        
        var noteRenderer = m_NoteRenderers[noteTransform];
        
        noteTransform.localScale = new Vector3(width, 1f, height);
        noteRenderer.SetPropertyBlock(m_NotePropertyBlocks[note.Channel % m_NotePropertyBlocks.Length]);
        
        m_ActiveNotes.Push(noteTransform);
    }
}
