using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ShirayuriMeshibe
{
    public abstract class EditorWindowSettings<T> : ScriptableSingleton<T> where T : ScriptableObject
    {
        [Serializable]
        public abstract class Property
        {
            internal abstract void Load();
            internal abstract void Reset();
            internal abstract void Apply();
        }

        [Serializable]
        public class Property<ValueType> : Property
        {
            [SerializeField] ValueType _value = default;

            //readonly EditorWindowSettings<T> _settings;
            readonly ValueType _defaultValue;

            public Property(EditorWindowSettings<T> settings, ValueType defaultValue)
            {
                _value = _defaultValue = defaultValue;
                //_settings = settings;
                settings._properties.Add(this);
            }

            public ValueType Value { get; set; }

            internal override void Load() => Value = _value;
            internal override void Reset() => Value = _defaultValue;
            internal override void Apply() => _value = Value;

            //public static implicit operator ValueType(Property<ValueType> rhs) { return rhs.Value; }
            //public static implicit operator Property<ValueType>(ValueType rhs) { return new Property<ValueType>(); }
        }

        List<Property> _properties = new List<Property>();

        protected EditorWindowSettings()
        {
        }

        public T Load()
        {
            foreach (var p in _properties)
                p.Load();
            return this as T;
        }

        public new void Save(bool saveAsText = true)
        {
            foreach (var p in _properties)
                p.Apply();
            base.Save(saveAsText);
        }

        public void Reset()
        {
            foreach (var p in _properties)
                p.Reset();
        }
    }
}
