using System;
using MidiPlayerTK;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    [SerializeField]
    RectTransform m_Panel;
    
    [SerializeField]
    Slider m_Slider;

    [SerializeField]
    Button m_GenerateButton;
    
    TextMeshProUGUI m_GenerateButtonText;

    [SerializeField]
    Button m_PlayButton;
    
    [SerializeField]
    OrbitCamera m_Camera;
    
    [SerializeField]
    MidiGen m_MidiGen;
    
    [SerializeField]
    PianoRoll m_PianoRoll;

    void Start()
    {
        m_GenerateButtonText = m_GenerateButton.GetComponentInChildren<TextMeshProUGUI>();
        
        m_GenerateButton.onClick.AddListener(OnGenerateButtonPressed);
        m_PlayButton.onClick.AddListener(OnPlayButtonPressed);
        
        m_MidiGen.OnNoteGenerated += OnNoteGenerated;
    }

    void OnNoteGenerated(MPTKEvent note)
    {
        m_PianoRoll.Add(note);
    }

    void OnGenerateButtonPressed()
    {
        m_MidiGen.CancelGeneration();
        
        if (m_MidiGen.IsGenerating)
        {
            return;
        }
        
        m_MidiGen.GenerateAsync();
    }
    
    void OnPlayButtonPressed()
    {
        m_PianoRoll.Clear();
        m_MidiGen.Play();
    }

    void Update()
    {
        m_GenerateButtonText.text = m_MidiGen.IsGenerating ? "Cancel" : "Generate";
        m_PlayButton.interactable = m_MidiGen.CanPlay;

        if (m_MidiGen.IsGenerating)
        {
            m_Slider.value = (float)m_MidiGen.CurrentGenerationLength / m_MidiGen.MaxLength;
        }

        m_Panel.gameObject.SetActive(!m_MidiGen.IsPlaying);
        m_Camera.enabled = m_MidiGen.IsPlaying;
    }
}
