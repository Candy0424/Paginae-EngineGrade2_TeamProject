using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Work.PSB.Code.SaveSystem
{
    [DefaultExecutionOrder(-5)]
    [Serializable]
    public class StageProgressData
    {
        public string stageName;
        public bool isCleared;
        public bool[] stars = new bool[3];
    }

    [DefaultExecutionOrder(-5)]
    [Serializable]
    public class StageProgressContainer
    {
        public List<StageProgressData> stages = new();
    }
    
    [DefaultExecutionOrder(-5)]
    public class StageProgressManager : MonoBehaviour
    {
        private static StageProgressManager _instance;
        public static StageProgressManager Instance => _instance;

        private string _savePath;
        private StageProgressContainer _container;

        private void Awake()
        {
            if (_instance != null)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            _savePath = Path.Combine(Application.persistentDataPath, "stage_progress.json");
            LoadData();
        }

        public StageProgressData GetStageData(string stageName)
        {
            return _container.stages.Find(s => s.stageName == stageName);
        }

        public void UpdateStage(string stageName, bool isCleared, bool[] stars)
        {
            var data = GetStageData(stageName);

            if (data == null)
            {
                data = new StageProgressData { stageName = stageName };
                _container.stages.Add(data);
            }

            data.isCleared |= isCleared;
            for (int i = 0; i < 3; i++)
                data.stars[i] |= stars[i];

            SaveData();
        }

        private void LoadData()
        {
            if (File.Exists(_savePath))
            {
                string json = File.ReadAllText(_savePath);
                _container = JsonUtility.FromJson<StageProgressContainer>(json);
            }
            else
            {
                _container = new StageProgressContainer();
                SaveData();
            }
        }

        private void SaveData()
        {
            string json = JsonUtility.ToJson(_container, true);
            File.WriteAllText(_savePath, json);
        }
        
        public int GetTotalStars()
        {
            int count = 0;
    
            foreach (var stage in _container.stages)
            {
                foreach (bool star in stage.stars)
                {
                    if (star) count++;
                }
            }

            return count;
        }

        public int GetMaxStars()
        {
            return 27;  //스테이지 수 * 3 방법이 생각안나서 그냥 넣음
        }
        
        private void Update()
        {
            #if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.B))
            {
                ResetAllStageData();
            }
            #endif
        }

        public void ResetAllStageData()
        {
            _container = new StageProgressContainer();
            SaveData();
        }
        
    }
}