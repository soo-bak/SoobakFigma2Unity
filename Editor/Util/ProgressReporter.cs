using UnityEditor;

namespace SoobakFigma2Unity.Editor.Util
{
    internal sealed class ProgressReporter
    {
        private readonly string _title;
        private int _totalSteps;
        private int _currentStep;

        public ProgressReporter(string title, int totalSteps)
        {
            _title = title;
            _totalSteps = totalSteps;
            _currentStep = 0;
        }

        public void SetTotal(int total) => _totalSteps = total;

        public void Step(string info)
        {
            _currentStep++;
            float progress = _totalSteps > 0 ? (float)_currentStep / _totalSteps : 0f;
            EditorUtility.DisplayProgressBar(_title, info, progress);
        }

        public void Done()
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
