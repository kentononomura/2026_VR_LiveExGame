using UnityEngine.UIElements;

namespace ShirayuriMeshibe.ParticleEmitterMeshGenerator
{
    public sealed class AreaField : VisualElement
    {
        private readonly TextField _textFieldName;
        private readonly Slider _sliderWeight;
        private readonly Label _labelHeadCount;
        private AreaSettings _areaSetting = null;

        public AreaField()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;

            _textFieldName = new TextField()
            {
                style =
                {
                    flexGrow = 1,
                    width = new StyleLength(new Length(1f, LengthUnit.Percent)),
                },
            };
            _textFieldName.value = "Area";
            Add(_textFieldName);

            _sliderWeight = new Slider()
            {
                style =
                {
                    flexGrow = 1,
                    width = new StyleLength(new Length(1f, LengthUnit.Percent)),
                },
            };
            _sliderWeight.lowValue = 0f;
            _sliderWeight.highValue = 1f;
            Add(_sliderWeight);

            var labelWeight = new Label()
            {
                style =
                {
                    flexGrow = 0.15f,
                    minWidth = 12f,
                    //width = new StyleLength(new Length(1f, LengthUnit.Percent)),
                },
            };
            labelWeight.text = $"{_sliderWeight.value:F2}";
            Add(labelWeight);

            _labelHeadCount = new Label()
            {
                style =
                {
                    flexGrow = 0.15f,
                    minWidth = 12f,
                    //width = new StyleLength(new Length(1f, LengthUnit.Percent)),
                },
            };
            _labelHeadCount.text = "0";
            Add(_labelHeadCount);

            _textFieldName.RegisterValueChangedCallback(e =>
            {
                if (_areaSetting != null)
                    _areaSetting.Name = _textFieldName.value;
                using (var areaFieldChangedEventArgs = AreaFieldChangedEventArgs.GetPooled())
                {
                    areaFieldChangedEventArgs.target = this;
                    SendEvent(areaFieldChangedEventArgs);
                }
            });
            _textFieldName.RegisterCallback<FocusOutEvent>(e =>
            {
                using (var areaFieldChangedEventArgs = AreaFieldChangedEventArgs.GetPooled())
                {
                    areaFieldChangedEventArgs.target = this;
                    SendEvent(areaFieldChangedEventArgs);
                }
            });

            _sliderWeight.RegisterValueChangedCallback(e =>
            {
                labelWeight.text = $"{_sliderWeight.value:F2}";

                if(_areaSetting != null)
                    _areaSetting.Weight = _sliderWeight.value;

                using (var areaFieldHeadCountChangedEventArgs = AreaFieldHeadCountChangedEventArgs.GetPooled())
                {
                    areaFieldHeadCountChangedEventArgs.target = this;
                    SendEvent(areaFieldHeadCountChangedEventArgs);
                }
            });
        }

        public void BindSetting(AreaSettings areaSettings)
        {
            _textFieldName.value = areaSettings.Name;
            _sliderWeight.value = areaSettings.Weight;
            _labelHeadCount.text = $"{areaSettings.Headcount}";
            _areaSetting = areaSettings;
        }
    }

    public class AreaFieldChangedEventArgs : EventBase<AreaFieldChangedEventArgs>
    {
        protected override void Init()
        {
            base.Init();
            this.bubbles = true;
            this.tricklesDown = false;
        }
    }

    public class AreaFieldHeadCountChangedEventArgs : EventBase<AreaFieldHeadCountChangedEventArgs>
    {
        protected override void Init()
        {
            base.Init();
            this.bubbles = true;
            this.tricklesDown = false;
        }
    }
}
