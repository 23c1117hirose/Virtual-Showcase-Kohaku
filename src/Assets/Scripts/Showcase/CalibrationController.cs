using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VirtualShowcase.Core;
using VirtualShowcase.Enums;
using VirtualShowcase.FaceTracking;
using VirtualShowcase.Utilities;

namespace VirtualShowcase.Showcase
{
    public class CalibrationController : MonoBehaviour
    {
        #region Serialized Fields

        [Header("UI Elements")]
        [SerializeField]
        private UnityEngine.Canvas canvas;

        [SerializeField]
        private Image monitorImage;

        [SerializeField]
        private Sprite[] monitorSprites;

        [SerializeField]
        private TMP_Text guideText;

        [SerializeField]
        private CameraPreview cameraPreview;

        [Header("Display distance sliders")]
        [SerializeField]
        private Slider distanceSlider;

        [Header("Display size sliders")]
        [SerializeField]
        private Slider sizeSlider;

        [SerializeField]
        private Detector detector;

        [SerializeField]
        private GameObject calibrationUIRoot; // Calibrationオブジェクト(手順UI全体の親)

        [SerializeField]
        private GameObject recalibrateButton; // RecalibrateButtonをドラッグ

        #endregion

        private CalibrationState _calibrationState = CalibrationState.Off;
        private string _nextStateKeybind;

        public bool Enabled => _calibrationState != CalibrationState.Off;

        #region Event Functions

        private void Start()
        {
            _nextStateKeybind = new InputActions().Calibration.Nextcalibration.GetBindingDisplayString();

            // Set sliders to the player prefs.
            distanceSlider.value = MyPrefs.ScreenDistance;
            sizeSlider.value = MyPrefs.ScreenSize;

            // Callbacks.
            distanceSlider.onValueChanged.AddListener(_ => ChangeDistanceValue(distanceSlider));
            sizeSlider.onValueChanged.AddListener(_ => ChangeSizeValue(sizeSlider));
        }

        #endregion

        public void ToggleCalibrationUI()
        {
            if (canvas.gameObject.activeSelf)
            {
                _calibrationState = CalibrationState.Off;
                TurnOffPreview();
                MyEvents.CalibrationChanged?.Invoke(gameObject, false);
            }
            else
            {
                TurnOnPreview();

                if (MyPrefs.CalibratedFocalLength > 0)
                {
                    calibrationUIRoot.SetActive(false);
                    recalibrateButton.SetActive(true);
                }
                else
                {
                    calibrationUIRoot.SetActive(true);
                    recalibrateButton.SetActive(false);
                    _calibrationState = CalibrationState.Left;
                    UpdateCalibrationState();
                    return;
                }

                MyEvents.CalibrationChanged?.Invoke(gameObject, true);
            }
        }

        public void SetNextState()
        {
            // If UI is disabled, don't continue.
            if (_calibrationState == CalibrationState.Off)
            {
                return;
            }

            _calibrationState = _calibrationState.Next();
            UpdateCalibrationState();
        }

        public void SetState(CalibrationState state)
        {
            _calibrationState = state;
            UpdateCalibrationState(false);
        }

        private void ChangeDistanceValue(Slider sender)
        {
            MyPrefs.ScreenDistance = (int)sender.value;
            MyEvents.ScreenDistanceChanged?.Invoke(gameObject, (int)sender.value);
        }

        private void ChangeSizeValue(Slider sender)
        {
            MyPrefs.ScreenSize = (int)sender.value;
            MyEvents.ScreenSizeChanged?.Invoke(gameObject, (int)sender.value);
        }

        private void UpdateCalibrationState(bool updatePrefs = true)
        {
            switch (_calibrationState)
            {
                // Highlight left edge.
                case CalibrationState.Left:
                    TurnOnPreview();
                    calibrationUIRoot.SetActive(true);
                    recalibrateButton.SetActive(false); // ← 追加(念のための保険)
                    SetGuideText("left");
                    HighlightEdge();
                    break;

                // Set left edge, highlight right edge.
                case CalibrationState.Right:
                    if (updatePrefs)
                    {
                        MyPrefs.LeftCalibration = detector.LastDetection.EyesCenter.x;
                    }

                    SetGuideText("right");
                    HighlightEdge();
                    break;

                // Set right edge, highlight bottom edge.
                case CalibrationState.Bottom:
                    if (updatePrefs)
                    {
                        MyPrefs.RightCalibration = detector.LastDetection.EyesCenter.x;
                    }

                    SetGuideText("bottom");
                    HighlightEdge();
                    break;

                // Set bottom edge, highlight top edge.
                case CalibrationState.Top:
                    if (updatePrefs)
                    {
                        MyPrefs.BottomCalibration = detector.LastDetection.EyesCenter.y;
                    }

                    SetGuideText("top");
                    HighlightEdge();
                    break;

                // Set top edge, highlight middle.
                case CalibrationState.Sliders:
                    if (updatePrefs)
                    {
                        MyPrefs.TopCalibration = detector.LastDetection.EyesCenter.y;
                    }

                    guideText.text =
                        $"Set the sliders and keep your head pointed at the display from the given distance, then press '{_nextStateKeybind}'";
                    HighlightEdge();
                    break;

                // Set focal length and hide UI.
                case CalibrationState.Reset:
                    MyPrefs.CalibratedFocalLength = detector.LastDetection.GetFocalLength(distanceSlider.value);
                    
                    // ...(既存の追加分はそのまま)...

                    TurnOffPreview(); // プレビューも手順UIも全部消す
                    calibrationUIRoot.SetActive(true); // 内部状態リセット(次回のため)

                    _calibrationState = CalibrationState.Off;
                    break;

                case CalibrationState.Off:
                    TurnOffPreview();
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            MyEvents.CalibrationChanged?.Invoke(gameObject, _calibrationState != CalibrationState.Off);
        }

        private void SetGuideText(string edgeText)
        {
            var text = $"Align your head with the {edgeText} edge of the display and press '{_nextStateKeybind}'";
            guideText.text = text;
        }

        private void TurnOnPreview()
        {
            canvas.gameObject.SetActive(true);
            cameraPreview.Enable(); // ShowSmallPreview() から変更
        }

        private void TurnOffPreview()
        {
            canvas.gameObject.SetActive(false);

            // Disable camera preview.
            cameraPreview.Disable();
        }

        private void HighlightEdge()
        {
            monitorImage.sprite = monitorSprites[(int)_calibrationState.Prev()];
        }

        public void StartRecalibration()
        {
            recalibrateButton.SetActive(false);
            calibrationUIRoot.SetActive(true);
            _calibrationState = CalibrationState.Left;
            UpdateCalibrationState();
        }
    }
}