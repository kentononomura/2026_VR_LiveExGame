using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShirayuriMeshibe.ParticleEmitterMeshGenerator
{
    public sealed class ParticleEmitterMeshGeneratorWindow : EditorWindow
    {
        [MenuItem("Tools/ShirayuriMeshibe/Particle Emitter Mesh Generator(Rectangle)")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow<ParticleEmitterMeshGeneratorWindow>("MeshGenerator");
        }

        private CancellationTokenSource _cancellationTokenSource;
        private void OnEnable()
        {
            if (_cancellationTokenSource == null)
                _cancellationTokenSource = new CancellationTokenSource();
        }
        private void OnDisable()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
        }
        public override void SaveChanges()
        {
            ParticleEmitterMeshGeneratorWindowSettings.instance.Save();
            base.SaveChanges();
        }

        private void CreateGUI()
        {
            var settings = ParticleEmitterMeshGeneratorWindowSettings.instance.Load();

            var contentContainer = new VisualElement
            {
                style =
                {
                    marginTop = 10f,
                    marginRight = 10f,
                    marginLeft = 10f,
                },
            };
            rootVisualElement.Add(contentContainer);

            var textFieldOutputPath = new TextField()
            {
            };
            textFieldOutputPath.label = "OutputFolderPath";
            textFieldOutputPath.value = settings.OutputFolderPath.Value;
            contentContainer.Add(textFieldOutputPath);

            var boxSettings = new Box()
            {
                style =
                {
                    marginTop = 5f,
                    marginBottom = 5f,
                },
            };
            contentContainer.Add(boxSettings);

            var horizontalContainer = new VisualElement()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.SpaceBetween,
                    alignItems = Align.Center,
                    marginBottom = 8f,
                },
            };
            boxSettings.Add(horizontalContainer);

            var labelSettings = new Label()
            {
                style =
                {
                    marginBottom = 3f,
                    fontSize = 13f,
                    unityFontStyleAndWeight = FontStyle.Bold,
                },
            };
            labelSettings.text = "Settings";
            horizontalContainer.Add(labelSettings);

            var buttonReset = new Button();
            buttonReset.text = "Reset";
            horizontalContainer.Add(buttonReset);

            var enumFieldMeshDirection = new EnumField(settings.MeshBuildDirection.Value);
            enumFieldMeshDirection.label = "MeshBuildDirection";
            boxSettings.Add(enumFieldMeshDirection);

            var enumSortType = new EnumField(settings.Sort.Value);
            enumSortType.label = "SortType";
            boxSettings.Add(enumSortType);

            var floatFieldAudienceDistanceX = new FloatField()
            {
            };
            floatFieldAudienceDistanceX.label = "AudienceDistance(X)";
            floatFieldAudienceDistanceX.value = settings.AudienceDistanceX.Value;
            boxSettings.Add(floatFieldAudienceDistanceX);

            var floatFieldAudienceDistanceZ = new FloatField()
            {
            };
            floatFieldAudienceDistanceZ.label = "AudienceDistance(Z)";
            floatFieldAudienceDistanceZ.value = settings.AudienceDistanceZ.Value;
            boxSettings.Add(floatFieldAudienceDistanceZ);

            var floatFieldAudienceRandomDistance = new FloatField()
            {
            };
            floatFieldAudienceRandomDistance.label = "RandomDistance(XZ)";
            floatFieldAudienceRandomDistance.value = settings.AudienceRandomDistance.Value;
            boxSettings.Add(floatFieldAudienceRandomDistance);

            var integerFieldAudienceCountX = new IntegerField();
            integerFieldAudienceCountX.label = "AudienceCount(X)";
            integerFieldAudienceCountX.value = settings.AudienceCountX.Value;
            boxSettings.Add(integerFieldAudienceCountX);

            var integerFieldAudienceCountZ = new IntegerField();
            integerFieldAudienceCountZ.label = "AudienceCount(Z)";
            integerFieldAudienceCountZ.value = settings.AudienceCountZ.Value;
            boxSettings.Add(integerFieldAudienceCountZ);

            var visualElementRandomSeed = new VisualElement()
            {
                style=
                {
                    flexDirection=FlexDirection.Row,
                    marginTop = 10f,
                    marginBottom = 10f,
                }
            };
            boxSettings.Add(visualElementRandomSeed);

            var integerFieldRandomSeed = new IntegerField()
            {
                style=
                {
                    flexGrow=1,
                },
            };
            integerFieldRandomSeed.label = "RandomSeed";
            integerFieldRandomSeed.value = settings.RandomSeed.Value;
            visualElementRandomSeed.Add(integerFieldRandomSeed);

            var buttonReseed = new Button();
            buttonReseed.text = "Reseed";
            visualElementRandomSeed.Add(buttonReseed);

            var labelAreaSettings = new Label()
            {
                style =
                {
                    marginTop = 5f,
                },
            };
            labelAreaSettings.text = "Area and Weight";
            boxSettings.Add(labelAreaSettings);

            var listViewArea = new ListView()
            {
                style =
                {
                    flexGrow = 1,
                    maxHeight = 192,
                    height = 192,
                },
            };
            boxSettings.Add(listViewArea);

            listViewArea.headerTitle = "Area";
            listViewArea.showBorder = true;
            listViewArea.horizontalScrollingEnabled = false;
            listViewArea.reorderable = false;
            listViewArea.selectionType = SelectionType.Single;
            listViewArea.itemsSource = settings.AreaSettings.Value;
            listViewArea.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            listViewArea.reorderMode = ListViewReorderMode.Simple;
            listViewArea.showAddRemoveFooter = true;

            // MultiColumnListViewにしたいがUnity2022からしか使えない。

            //----------------
            // OutputMetrices
            //----------------

            var boxOutputMetrices = new Box()
            {
                style =
                {
                    marginTop = 10f,
                },
            };
            contentContainer.Add(boxOutputMetrices);

            var labelMetricsInformation = new Label()
            {
                style =
                {
                    marginBottom = 6f,
                    fontSize = 13f,
                    unityFontStyleAndWeight = FontStyle.Bold,
                },
            };
            labelMetricsInformation.text = "OutputMetrics";
            boxOutputMetrices.Add(labelMetricsInformation);

            var floatFieldMeshWidth = new FloatField()
            {
            };
            floatFieldMeshWidth.label = "Width(X)";
            floatFieldMeshWidth.value = floatFieldAudienceDistanceX.value * integerFieldAudienceCountX.value;
            floatFieldMeshWidth.isReadOnly = true;
            floatFieldMeshWidth.focusable = false;
            boxOutputMetrices.Add(floatFieldMeshWidth);

            var floatFieldMeshDepth = new FloatField()
            {
            };
            floatFieldMeshDepth.label = "Depth(Z)";
            floatFieldMeshDepth.value = floatFieldAudienceDistanceZ.value * integerFieldAudienceCountZ.value;
            floatFieldMeshDepth.isReadOnly = true;
            floatFieldMeshDepth.focusable = false;
            boxOutputMetrices.Add(floatFieldMeshDepth);

            var integerFieldAudienceCountTotal = new IntegerField();
            integerFieldAudienceCountTotal.label = "TotalAudienceCount";
            integerFieldAudienceCountTotal.value = integerFieldAudienceCountX.value * integerFieldAudienceCountZ.value;
            integerFieldAudienceCountTotal.isReadOnly = true;
            integerFieldAudienceCountTotal.focusable = false;
            boxOutputMetrices.Add(integerFieldAudienceCountTotal);

            var space = new VisualElement
            {
                style =
                {
                    flexGrow = new StyleFloat(1f),
                },
            };
            rootVisualElement.Add(space);

            var progressBar = new ProgressBar();
            progressBar.title = "0.00%";
            rootVisualElement.Add(progressBar);

            var buttonGenerate = new Button();
            buttonGenerate.text = "Generate";
            buttonGenerate.style.marginBottom = 10f;
            rootVisualElement.Add(buttonGenerate);

            var helpBox = new HelpBox()
            {
                style =
                {
                    display = DisplayStyle.None,
                },
            };
            rootVisualElement.Add(helpBox);

            //---------
            // Action
            //---------

            Func<bool> isButtonEnabled = () =>
            {
                var stringBuilder = new StringBuilder();

                if (String.IsNullOrWhiteSpace(textFieldOutputPath.value))
                    stringBuilder.AppendLine("OutputPathを設定してください。");

                if(!AssetDatabase.IsValidFolder(textFieldOutputPath.value))
                    stringBuilder.AppendLine("OutputPathが不正なパスです。");

                var areaSettings = settings.AreaSettings.Value;
                if (areaSettings.Length == 0)
                    stringBuilder.AppendLine("Area and Weightが設定されていません。");

                HashSet<string> AreaNames = new();
                foreach(var areaSetting in areaSettings)
                {
                    if(AreaNames.Contains(areaSetting.Name))
                    {
                        stringBuilder.AppendLine($"Area and Weightの名前が重複しています。{areaSetting.Name}");
                        break;
                    }
                    else
                        AreaNames.Add(areaSetting.Name);
                }

                var totalWeights = areaSettings.Sum(x => x.Weight);
                if(Mathf.Approximately(totalWeights, 0f))
                    stringBuilder.AppendLine($"Weightを設定してください。");

                if (0 < stringBuilder.Length)
                {
                    helpBox.messageType = HelpBoxMessageType.Warning;
                    helpBox.text = stringBuilder.ToString();
                    helpBox.style.display = DisplayStyle.Flex;
                    return false;
                }

                helpBox.style.display = DisplayStyle.None;
                return true;
            };

            textFieldOutputPath.RegisterValueChangedCallback(e =>
            {
                buttonGenerate.SetEnabled(isButtonEnabled());
            });

            floatFieldAudienceDistanceX.RegisterValueChangedCallback(e =>
            {
                floatFieldAudienceDistanceX.SetValueWithoutNotify(Mathf.Max(0f, e.newValue));
                floatFieldMeshWidth.value = floatFieldAudienceDistanceX.value * integerFieldAudienceCountX.value;
            });

            floatFieldAudienceDistanceZ.RegisterValueChangedCallback(e =>
            {
                floatFieldAudienceDistanceZ.SetValueWithoutNotify(Mathf.Max(0f, e.newValue));
                floatFieldMeshDepth.value = floatFieldAudienceDistanceZ.value * integerFieldAudienceCountZ.value;
            });

            floatFieldAudienceRandomDistance.RegisterCallback<FocusOutEvent>(e =>
            {
                floatFieldAudienceRandomDistance.SetValueWithoutNotify(Mathf.Max(0f, floatFieldAudienceRandomDistance.value));
            });

            integerFieldAudienceCountX.RegisterValueChangedCallback(e =>
            {
                integerFieldAudienceCountX.SetValueWithoutNotify(Mathf.Max(1, e.newValue));
                floatFieldMeshWidth.value = floatFieldAudienceDistanceX.value * integerFieldAudienceCountX.value;
                integerFieldAudienceCountTotal.value = integerFieldAudienceCountX.value * integerFieldAudienceCountZ.value;
            });

            integerFieldAudienceCountZ.RegisterValueChangedCallback(e =>
            {
                integerFieldAudienceCountZ.SetValueWithoutNotify(Mathf.Max(1, e.newValue));
                floatFieldMeshDepth.value = floatFieldAudienceDistanceZ.value * integerFieldAudienceCountZ.value;
                integerFieldAudienceCountTotal.value = integerFieldAudienceCountX.value * integerFieldAudienceCountZ.value;
            });

            buttonReset.clicked += () =>
            {
                settings.Reset();
                enumFieldMeshDirection.value = settings.MeshBuildDirection.Value;
                enumSortType.value = settings.Sort.Value;
                floatFieldAudienceDistanceX.value = settings.AudienceDistanceX.Value;
                floatFieldAudienceDistanceZ.value = settings.AudienceDistanceZ.Value;
                floatFieldAudienceRandomDistance.value = settings.AudienceRandomDistance.Value;
                integerFieldAudienceCountX.value = settings.AudienceCountX.Value;
                integerFieldAudienceCountZ.value = settings.AudienceCountZ.Value;
                listViewArea.itemsSource = settings.AreaSettings.Value;
                listViewArea.RefreshItems();
                buttonGenerate.SetEnabled(isButtonEnabled());
            };

            buttonReseed.clicked += () =>
            {
                integerFieldRandomSeed.SetValueWithoutNotify(UnityEngine.Random.Range(0, 1000000));
            };

            buttonGenerate.clicked += () =>
            {
                settings.OutputFolderPath.Value = textFieldOutputPath.value;
                settings.MeshBuildDirection.Value = (MeshBuildDirectionType)enumFieldMeshDirection.value;
                settings.Sort.Value = (SortType)enumSortType.value;
                settings.AudienceDistanceX.Value = floatFieldAudienceDistanceX.value;
                settings.AudienceDistanceZ.Value = floatFieldAudienceDistanceZ.value;
                settings.AudienceRandomDistance.Value = floatFieldAudienceRandomDistance.value;
                settings.AudienceCountX.Value = integerFieldAudienceCountX.value;
                settings.AudienceCountZ.Value = integerFieldAudienceCountZ.value;
                settings.RandomSeed.Value = integerFieldRandomSeed.value;
                settings.Save();

                var areaSettings1 = settings.AreaSettings.Value;
                var areaSettings2 = new List<AreaSettings>(areaSettings1.Length);
                for(var i=0; i<areaSettings1.Length; ++i)
                {
                    var areaSetting = areaSettings1[i];
                    if (Mathf.Approximately(areaSetting.Weight, 0f))
                        continue;
                    areaSettings2.Add(new AreaSettings() { Name= areaSetting.Name, Weight=areaSetting.Weight, Headcount=areaSetting.Headcount });
                }

                OnPressedButtonGenerate(buttonGenerate,
                                        progressBar,
                                        textFieldOutputPath.value,
                                        (MeshBuildDirectionType)enumFieldMeshDirection.value,
                                        (SortType)enumSortType.value,
                                        floatFieldAudienceDistanceX.value,
                                        floatFieldAudienceDistanceZ.value,
                                        floatFieldAudienceRandomDistance.value,
                                        integerFieldAudienceCountX.value,
                                        integerFieldAudienceCountZ.value,
                                        areaSettings2.ToArray(),
                                        integerFieldRandomSeed.value,
                                        settings,
                                        _cancellationTokenSource.Token);
            };

            buttonGenerate.SetEnabled(isButtonEnabled());

            //----------
            // ListView
            //----------

            void UpdateListViewItem()
            {
                var areaSettings = settings.AreaSettings.Value;
                var totalHeadCount = integerFieldAudienceCountX.value * integerFieldAudienceCountZ.value;
                var totalWeight = areaSettings.Sum(x => x.Weight);
                if (0f < totalWeight)
                {
                    var normalizedWeights = areaSettings.Select(x => x.Weight / totalWeight).ToArray();
                    var totalHeadCount2 = 0;
                    for (int i = 0; i < areaSettings.Length; ++i)
                    {
                        areaSettings[i].Headcount = Mathf.FloorToInt(normalizedWeights[i] * totalHeadCount);
                        totalHeadCount2 += areaSettings[i].Headcount;
                    }
                    if (totalHeadCount != totalHeadCount2 && 0 < areaSettings.Length)
                    {
                        areaSettings[areaSettings.Length - 1].Headcount += totalHeadCount - totalHeadCount2;
                    }
                }
                else
                {
                    foreach (var areaSetting in areaSettings)
                        areaSetting.Headcount = 0;
                    buttonGenerate.SetEnabled(isButtonEnabled());
                }
            }

            listViewArea.makeItem = () =>
            {
                var item = new AreaField();
                return item;
            };

            listViewArea.bindItem = (visualElement, index) =>
            {
                var areaField = visualElement as AreaField;
                var areaSettings = settings.AreaSettings.Value;
                if (areaSettings.Length <= index)
                {
                    Array.Resize(ref areaSettings, index + 1);
                    areaSettings[index] = new AreaSettings() { Name = $"Area{index}" };
                    settings.AreaSettings.Value = areaSettings;
                }
                var areaSetting = settings.AreaSettings.Value[index];
                areaField.BindSetting(areaSetting);

                // Memo) ItemAdded, itemsSourceChangedではItemSourceが更新されていない
                buttonGenerate.SetEnabled(isButtonEnabled());
            };

            listViewArea.itemsRemoved += indexes =>
            {
                var list = new List<AreaSettings>(settings.AreaSettings.Value);
                var indexesArray = indexes.ToArray();
                Array.Reverse(indexesArray);
                foreach (var index in indexesArray)
                    list.RemoveAt(index);
                settings.AreaSettings.Value = list.ToArray();
                UpdateListViewItem();
                buttonGenerate.SetEnabled(isButtonEnabled());
            };

            listViewArea.itemIndexChanged += (index1, index2) =>
            {
                var areaSettings = settings.AreaSettings.Value;
                if (areaSettings.Length <= index1 || areaSettings.Length <= index2)
                {
                    Debug.LogError($"Invalid indexes. index1:{index1}, index2:{index2}");
                    return;
                }
                var areaSetting = areaSettings[index2];
                areaSettings[index2] = areaSettings[index1];
                areaSettings[index1] = areaSetting;
            };

            listViewArea.RegisterCallback<AreaFieldChangedEventArgs>(e =>
            {
                e.StopPropagation();
                buttonGenerate.SetEnabled(isButtonEnabled());
            });

            listViewArea.RegisterCallback<AreaFieldHeadCountChangedEventArgs>(e =>
            {
                e.StopPropagation();
                UpdateListViewItem();
                listViewArea.RefreshItems();
            });
        }

        async void OnPressedButtonGenerate(Button button,
                                           ProgressBar progressBar,
                                           string outputPath,
                                           MeshBuildDirectionType meshBuildDirectionType,
                                           SortType sortType,
                                           float audienceDistanceX,
                                           float audienceDistanceZ,
                                           float audienceRandomDistance,
                                           int audienceCountX,
                                           int audienceCountZ,
                                           AreaSettings[] areaSettings,
                                           int randomSeed,
                                           ParticleEmitterMeshGeneratorWindowSettings particleEmitterMeshGeneratorWindowSettings,
                                           CancellationToken cancellationToken)
        {
            var progress = new Action<string, float>((title, value) =>
            {
                progressBar.value = value;
                progressBar.title = $"{title}-{value:##0.00}%";
            });

            try
            {
                progress("Ready...", 0f);
                button.SetEnabled(false);

                var meshes = await ParticleEmitterMeshGenerator.Generate(progress,
                                                                         outputPath,
                                                                         meshBuildDirectionType,
                                                                         sortType,
                                                                         audienceDistanceX,
                                                                         audienceDistanceZ,
                                                                         audienceRandomDistance,
                                                                         audienceCountX,
                                                                         audienceCountZ,
                                                                         areaSettings,
                                                                         randomSeed,
                                                                         cancellationToken);

                if(cancellationToken.IsCancellationRequested)
                    return;

                if (meshes == null)
                    return;

                var suffix = $"{DateTime.Now:yyyyMMddHHmmss}";

                foreach(var mesh in meshes)
                {
                    var path = $"{outputPath}/{suffix}_{mesh.name}_VertexCount({mesh.vertexCount}).mesh";
                    path = AssetDatabase.GenerateUniqueAssetPath(path);

                    AssetDatabase.CreateAsset(mesh, path);
                    AssetDatabase.SaveAssets();

                    ModifyAssetProperties(path);
                    Debug.Log($"Generated mesh. path:{path}", mesh);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                // Export settings to json
                {
                    var path = $"{outputPath}/{suffix}_ExportSettings.json";
                    path = AssetDatabase.GenerateUniqueAssetPath(path);
                    var fullpath = Path.GetFullPath(path);
                    particleEmitterMeshGeneratorWindowSettings.WriteToJson(fullpath);
                    AssetDatabase.ImportAsset(path);
                }

                progress("Complete", 100f);
            }
            finally
            {
                button.SetEnabled(true);
            }
        }

        void ModifyAssetProperties(string meshPath)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                Debug.LogError($"Failed load mesh assets. path:{meshPath}");
                return;
            }
            using (var serializedObject = new SerializedObject(mesh))
            {
                var property = serializedObject.FindProperty("m_IsReadable");
                property.boolValue = false;
                serializedObject.ApplyModifiedProperties();
            }
            EditorGUIUtility.PingObject(mesh);
        }
    }
}
