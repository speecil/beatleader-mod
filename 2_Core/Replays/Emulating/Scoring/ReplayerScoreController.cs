﻿using System;
using System.Linq;
using System.Collections.Generic;
using static ScoreMultiplierCounter;
using BeatLeader.Replays.Emulating;
using BeatLeader.Models;
using BeatLeader.Utils;
using UnityEngine;
using Zenject;

namespace BeatLeader.Replays.Scoring
{
    public class ReplayerScoreController : MonoBehaviour, IReplayerScoreController
    {
        [Inject] private readonly Replay _replay;
        [Inject] private readonly SimpleNoteComparatorsSpawner _simpleNoteComparatorsSpawner;
        [Inject] private readonly ReplayerManualInstaller.InitData _initData;
        [Inject] private readonly BeatmapTimeController _beatmapTimeController;
        [Inject] private readonly IScoringInterlayer _scoringInterlayer;
        [Inject] private readonly IReadonlyBeatmapData _beatmapData;

        private GameplayModifiersModelSO _gameplayModifiersModel;
        private List<GameplayModifierParamsSO> _gameplayModifierParams;

        private int _maxComboAfterRescoring;
        private int _comboAfterRescoring;

        public int MaxComboAfterRescoring => _maxComboAfterRescoring;
        public int ComboAfterRescoring => _comboAfterRescoring;

        public event Action<int, int, bool> OnComboChangedAfterRescoring;

        #region BaseGame stuff
        [Inject] private readonly GameplayModifiers _gameplayModifiers;
        [Inject] private readonly BeatmapObjectManager _beatmapObjectManager;
        [Inject] private readonly IGameEnergyCounter _gameEnergyCounter;
        [Inject] private readonly AudioTimeSyncController _audioTimeSyncController;
        [Inject] private readonly BadCutScoringElement.Pool _badCutScoringElementPool;
        [Inject] private readonly MissScoringElement.Pool _missScoringElementPool;
        [Inject] private readonly GoodCutScoringElement.Pool _goodCutScoringElementPool;
        [Inject] private readonly PlayerHeadAndObstacleInteraction _playerHeadAndObstacleInteraction;

        private readonly ScoreMultiplierCounter _maxScoreMultiplierCounter = new ScoreMultiplierCounter();
        private readonly ScoreMultiplierCounter _scoreMultiplierCounter = new ScoreMultiplierCounter();
        private readonly List<float> _sortedNoteTimesWithoutScoringElements = new List<float>(50);
        private readonly List<ScoringElement> _sortedScoringElementsWithoutMultiplier = new List<ScoringElement>(50);
        private readonly List<ScoringElement> _scoringElementsWithMultiplier = new List<ScoringElement>(50);
        private readonly List<ScoringElement> _scoringElementsToRemove = new List<ScoringElement>(50);

        private int _modifiedScore;
        private int _multipliedScore;
        private int _immediateMaxPossibleMultipliedScore;
        private int _immediateMaxPossibleModifiedScore;
        private float _prevMultiplierFromModifiers;

        public int multipliedScore => _multipliedScore;
        public int modifiedScore => _modifiedScore;
        public int immediateMaxPossibleMultipliedScore => _immediateMaxPossibleMultipliedScore;
        public int immediateMaxPossibleModifiedScore => _immediateMaxPossibleModifiedScore;

        public event Action<int, int> scoreDidChangeEvent;
        public event Action<int, float> multiplierDidChangeEvent;
        public event Action<ScoringElement> scoringForNoteStartedEvent;
        public event Action<ScoringElement> scoringForNoteFinishedEvent;
        #endregion

        public virtual void Start()
        {
            _gameplayModifiersModel = Resources.FindObjectsOfTypeAll<GameplayModifiersModelSO>().First();
            _gameplayModifierParams = _gameplayModifiersModel.CreateModifierParamsList(_gameplayModifiers);
            _prevMultiplierFromModifiers = _gameplayModifiersModel.GetTotalMultiplier(_gameplayModifierParams, _gameEnergyCounter.energy);
            _playerHeadAndObstacleInteraction.headDidEnterObstaclesEvent += HandlePlayerHeadDidEnterObstacles;
            _beatmapObjectManager.noteWasCutEvent += HandleNoteWasCut;
            _beatmapObjectManager.noteWasMissedEvent += HandleNoteWasMissed;
            _beatmapObjectManager.noteWasSpawnedEvent += HandleNoteWasSpawned;
            _beatmapTimeController.OnSongRewind += RescoreInTimeSpan;
        }
        public virtual void OnDestroy()
        {
            if (_playerHeadAndObstacleInteraction != null)
            {
                _playerHeadAndObstacleInteraction.headDidEnterObstaclesEvent -= HandlePlayerHeadDidEnterObstacles;
            }

            if (_beatmapObjectManager != null)
            {
                _beatmapObjectManager.noteWasCutEvent -= HandleNoteWasCut;
                _beatmapObjectManager.noteWasMissedEvent -= HandleNoteWasMissed;
                _beatmapObjectManager.noteWasSpawnedEvent -= HandleNoteWasSpawned;
            }

            if (_beatmapTimeController != null)
            {
                _beatmapTimeController.OnSongRewind -= RescoreInTimeSpan;
            }
        }
        public virtual void LateUpdate()
        {
            float num = (_sortedNoteTimesWithoutScoringElements.Count > 0) ? _sortedNoteTimesWithoutScoringElements[0] : float.MaxValue;
            float num2 = _audioTimeSyncController.songTime + 0.15f;
            int num3 = 0;
            bool flag = false;
            foreach (ScoringElement item in _sortedScoringElementsWithoutMultiplier)
            {
                if (item.time < num2 || item.time > num)
                {
                    flag |= _scoreMultiplierCounter.ProcessMultiplierEvent(item.multiplierEventType);
                    if (item.wouldBeCorrectCutBestPossibleMultiplierEventType == ScoreMultiplierCounter.MultiplierEventType.Positive)
                    {
                        _maxScoreMultiplierCounter.ProcessMultiplierEvent(ScoreMultiplierCounter.MultiplierEventType.Positive);
                    }

                    item.SetMultipliers(_scoreMultiplierCounter.multiplier, _maxScoreMultiplierCounter.multiplier);
                    _scoringElementsWithMultiplier.Add(item);
                    num3++;
                    continue;
                }

                break;
            }

            _sortedScoringElementsWithoutMultiplier.RemoveRange(0, num3);
            if (flag)
            {
                multiplierDidChangeEvent?.Invoke(_scoreMultiplierCounter.multiplier, _scoreMultiplierCounter.normalizedProgress);
            }

            bool flag2 = false;
            _scoringElementsToRemove.Clear();
            foreach (ScoringElement item2 in _scoringElementsWithMultiplier)
            {
                if (item2.isFinished)
                {
                    if (item2.maxPossibleCutScore > 0f)
                    {
                        flag2 = true;
                        _multipliedScore += item2.cutScore * item2.multiplier;
                        _immediateMaxPossibleMultipliedScore += item2.maxPossibleCutScore * item2.maxMultiplier;
                    }

                    _scoringElementsToRemove.Add(item2);
                    scoringForNoteFinishedEvent?.Invoke(item2);
                }
            }

            foreach (ScoringElement item3 in _scoringElementsToRemove)
            {
                DespawnScoringElement(item3);
                _scoringElementsWithMultiplier.Remove(item3);
            }

            _scoringElementsToRemove.Clear();
            float totalMultiplier = _gameplayModifiersModel.GetTotalMultiplier(_gameplayModifierParams, _gameEnergyCounter.energy);
            if (_prevMultiplierFromModifiers != totalMultiplier)
            {
                _prevMultiplierFromModifiers = totalMultiplier;
                flag2 = true;
            }
            if (flag2)
            {
                _modifiedScore = ScoreModel.GetModifiedScoreForGameplayModifiersScoreMultiplier(_multipliedScore, totalMultiplier);
                _immediateMaxPossibleModifiedScore = ScoreModel.GetModifiedScoreForGameplayModifiersScoreMultiplier(_immediateMaxPossibleMultipliedScore, totalMultiplier);
                scoreDidChangeEvent?.Invoke(_multipliedScore, _modifiedScore);
            }
        }
        public virtual void RescoreInTimeSpan(float endTime)
        {
            List<BeatmapDataItem> filteredBeatmapItems = _beatmapData
               .GetFilteredCopy(x => x.time >= _audioTimeSyncController.songTimeOffset && x.time <= endTime
               && x.type == BeatmapDataItem.BeatmapDataItemType.BeatmapObject ? x : null).allBeatmapDataItems.ToList();

            _modifiedScore = 0;
            _multipliedScore = 0;
            _immediateMaxPossibleModifiedScore = 0;
            _immediateMaxPossibleMultipliedScore = 0;
            _comboAfterRescoring = 0;
            _maxComboAfterRescoring = 0;
            _scoreMultiplierCounter.Reset();
            _maxScoreMultiplierCounter.Reset();

            bool broke = false;
            foreach (BeatmapDataItem item in filteredBeatmapItems)
            {
                NoteData noteData;
                NoteEvent noteEvent;
                if ((noteData = item as NoteData) != null && (noteEvent = noteData.GetNoteEvent(_replay)) != null)
                {
                    switch (noteEvent.eventType)
                    {
                        case NoteEventType.good:
                            {
                                _scoreMultiplierCounter.ProcessMultiplierEvent(MultiplierEventType.Positive);
                                if (noteData.ComputeNoteMultiplier() == MultiplierEventType.Positive)
                                    _maxScoreMultiplierCounter.ProcessMultiplierEvent(MultiplierEventType.Positive);

                                int totalScore = noteEvent.ComputeNoteScore();
                                int maxPossibleScore = ScoreModel.GetNoteScoreDefinition(noteData.scoringType).maxCutScore;

                                _multipliedScore += totalScore * _scoreMultiplierCounter.multiplier;
                                _immediateMaxPossibleMultipliedScore += maxPossibleScore * _maxScoreMultiplierCounter.multiplier;
                                _comboAfterRescoring++;
                                _maxComboAfterRescoring = _comboAfterRescoring > _maxComboAfterRescoring ? _comboAfterRescoring : _maxComboAfterRescoring;

                                float totalMultiplier = _gameplayModifiersModel.GetTotalMultiplier(_gameplayModifierParams, _gameEnergyCounter.energy);
                                _prevMultiplierFromModifiers = _prevMultiplierFromModifiers != totalMultiplier ? totalMultiplier : _prevMultiplierFromModifiers;

                                _modifiedScore = ScoreModel.GetModifiedScoreForGameplayModifiersScoreMultiplier(_multipliedScore, totalMultiplier);
                                _immediateMaxPossibleModifiedScore = ScoreModel.GetModifiedScoreForGameplayModifiersScoreMultiplier(_immediateMaxPossibleMultipliedScore, totalMultiplier);
                            }
                            break;
                        case NoteEventType.bad:
                        case NoteEventType.miss:
                            _scoreMultiplierCounter.ProcessMultiplierEvent(MultiplierEventType.Negative);
                            _comboAfterRescoring = 0;
                            broke = true;
                            break;
                        case NoteEventType.bomb:
                            _scoreMultiplierCounter.ProcessMultiplierEvent(MultiplierEventType.Negative);
                            break;
                        default: throw new Exception("Unknown note type exception!");
                    }
                    continue;
                }

                ObstacleData obstacleData;
                WallEvent wallEvent;
                if ((obstacleData = item as ObstacleData) == null || (wallEvent = obstacleData.GetWallEvent(_replay)) == null) continue;

                _scoreMultiplierCounter.ProcessMultiplierEvent(MultiplierEventType.Negative);
                _comboAfterRescoring = 0;
                broke = true;
            }

            OnComboChangedAfterRescoring?.Invoke(_comboAfterRescoring, _maxComboAfterRescoring, broke);
            scoreDidChangeEvent?.Invoke(_multipliedScore, _modifiedScore);
            multiplierDidChangeEvent?.Invoke(_scoreMultiplierCounter.multiplier, _scoreMultiplierCounter.normalizedProgress);
        }
        public virtual void HandleNoteWasSpawned(NoteController noteController)
        {
            if (noteController.noteData.scoringType == NoteData.ScoringType.Ignore) return;
            ListExtensions.InsertIntoSortedListFromEnd(_sortedNoteTimesWithoutScoringElements, noteController.noteData.time);
        }
        public virtual void HandleNoteWasCut(NoteController noteController, in NoteCutInfo noteCutInfo)
        {
            if (noteCutInfo.noteData.scoringType == NoteData.ScoringType.Ignore) return;
            if (noteCutInfo.allIsOK)
            {
                ScoringData scoringData = new ScoringData();
                if (_simpleNoteComparatorsSpawner != null && _simpleNoteComparatorsSpawner
                    .TryGetLoadedComparator(noteController, out SimpleNoteCutComparator comparator))
                {
                    scoringData = new ScoringData(comparator.NoteController.noteData, comparator.NoteCutEvent,
                        comparator.NoteController.worldRotation, comparator.NoteController.inverseWorldRotation,
                        comparator.NoteController.noteTransform.localRotation, comparator.NoteController.noteTransform.position);
                    comparator.Dispose();
                }
                else
                    scoringData = new ScoringData(noteController, noteController.GetNoteEvent(_replay));

                ScoringElement scoringElement = _scoringInterlayer.Convert<GoodCutScoringElement>(scoringData);
                ListExtensions.InsertIntoSortedListFromEnd(_sortedScoringElementsWithoutMultiplier, scoringElement);
                scoringForNoteStartedEvent?.Invoke(scoringElement);
                _sortedNoteTimesWithoutScoringElements.Remove(noteCutInfo.noteData.time);
            }
            else
            {
                BadCutScoringElement badCutScoringElement = _badCutScoringElementPool.Spawn();
                badCutScoringElement.Init(noteCutInfo.noteData);
                ListExtensions.InsertIntoSortedListFromEnd(_sortedScoringElementsWithoutMultiplier, badCutScoringElement);
                scoringForNoteStartedEvent?.Invoke(badCutScoringElement);
                _sortedNoteTimesWithoutScoringElements.Remove(noteCutInfo.noteData.time);
            }
        }
        public virtual void HandleNoteWasMissed(NoteController noteController)
        {
            NoteData noteData = noteController.noteData;
            if (noteData.scoringType != NoteData.ScoringType.Ignore)
            {
                MissScoringElement missScoringElement = _missScoringElementPool.Spawn();
                missScoringElement.Init(noteData);
                ListExtensions.InsertIntoSortedListFromEnd(_sortedScoringElementsWithoutMultiplier, missScoringElement);
                scoringForNoteStartedEvent?.Invoke(missScoringElement);
                _sortedNoteTimesWithoutScoringElements.Remove(noteData.time);
            }
        }
        public virtual void HandlePlayerHeadDidEnterObstacles()
        {
            if (_scoreMultiplierCounter.ProcessMultiplierEvent(MultiplierEventType.Negative))
            {
                multiplierDidChangeEvent?.Invoke(_scoreMultiplierCounter.multiplier, _scoreMultiplierCounter.normalizedProgress);
            }
        }
        public virtual void DespawnScoringElement(ScoringElement scoringElement)
        {
            if (scoringElement != null)
            {
                GoodCutScoringElement goodCutScoringElement;
                if ((goodCutScoringElement = scoringElement as GoodCutScoringElement) != null)
                {
                    GoodCutScoringElement item = goodCutScoringElement;
                    _goodCutScoringElementPool.Despawn(item);
                    return;
                }

                BadCutScoringElement badCutScoringElement;
                if ((badCutScoringElement = scoringElement as BadCutScoringElement) != null)
                {
                    BadCutScoringElement item2 = badCutScoringElement;
                    _badCutScoringElementPool.Despawn(item2);
                    return;
                }

                MissScoringElement missScoringElement;
                if ((missScoringElement = scoringElement as MissScoringElement) != null)
                {
                    MissScoringElement item3 = missScoringElement;
                    _missScoringElementPool.Despawn(item3);
                    return;
                }
            }

            throw new ArgumentOutOfRangeException();
        }
        public virtual void SetEnabled(bool enabled)
        {
            base.enabled = enabled;
        }
    }
}
