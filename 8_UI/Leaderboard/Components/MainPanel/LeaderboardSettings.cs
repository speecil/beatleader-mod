using BeatLeader.Manager;
using BeatLeader.Models;
using BeatSaberMarkupLanguage.Attributes;
using HMUI;
using JetBrains.Annotations;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BeatLeader.Components {
    internal class LeaderboardSettings : ReeUIComponentV2 {
        #region Init / Dispose

        protected override void OnInitialize() {
            LeaderboardEvents.LogoWasPressedEvent += HideInstantly;
            LeaderboardEvents.AvatarWasPressedEvent += OnAvatarWasPressed;
            LeaderboardEvents.SceneTransitionStartedEvent += HideInstantly;
            LeaderboardState.IsVisibleChangedEvent += OnLeaderboardVisibilityChanged;
            ApplyScale();
        }

        protected override void OnDispose() {
            LeaderboardEvents.LogoWasPressedEvent -= HideInstantly;
            LeaderboardEvents.AvatarWasPressedEvent -= OnAvatarWasPressed;
            LeaderboardEvents.SceneTransitionStartedEvent -= HideInstantly;
            LeaderboardState.IsVisibleChangedEvent -= OnLeaderboardVisibilityChanged;
        }

        #endregion

        #region Events

        private void OnAvatarWasPressed() {
            ShowModal();
        }

        private void OnLeaderboardVisibilityChanged(bool isVisible) {
            if (!isVisible) HideAnimated();
        }

        #endregion

        #region Modal

        [UIComponent("modal"), UsedImplicitly]
        private ModalView _modal;

        private void ShowModal() {
            if (_modal == null) return;
            _modal.Show(true, true);
        }

        private void HideInstantly() {
            if (_modal == null) return;
            _modal.Hide(false);
        }

        private void HideAnimated() {
            if (_modal == null) return;
            _modal.Hide(true);
        }

        #endregion

        #region Container

        private const float Scale = 0.8f;

        [UIComponent("container"), UsedImplicitly]
        private RectTransform _containerTransform;

        private void ApplyScale() {
            _containerTransform.localScale = new Vector3(Scale, Scale, Scale);
        }

        #endregion

        #region Toggles

        [UIValue("avatar-mask-value"), UsedImplicitly]
        private bool AvatarMaskValue {
            get => PluginConfig.LeaderboardTableMask.HasFlag(ScoreRowCellType.Avatar);
            set {
                if (value) {
                    PluginConfig.LeaderboardTableMask |= ScoreRowCellType.Avatar;
                } else {
                    PluginConfig.LeaderboardTableMask &= ~ScoreRowCellType.Avatar;
                }
            }
        }

        [UIValue("country-mask-value"), UsedImplicitly]
        private bool CountryMaskValue {
            get => PluginConfig.LeaderboardTableMask.HasFlag(ScoreRowCellType.Country);
            set {
                if (value) {
                    PluginConfig.LeaderboardTableMask |= ScoreRowCellType.Country;
                } else {
                    PluginConfig.LeaderboardTableMask &= ~ScoreRowCellType.Country;
                }
            }
        }

        [UIValue("clans-mask-value"), UsedImplicitly]
        private bool ClansMaskValue {
            get => PluginConfig.LeaderboardTableMask.HasFlag(ScoreRowCellType.Clans);
            set {
                if (value) {
                    PluginConfig.LeaderboardTableMask |= ScoreRowCellType.Clans;
                } else {
                    PluginConfig.LeaderboardTableMask &= ~ScoreRowCellType.Clans;
                }
            }
        }

        [UIValue("score-mask-value"), UsedImplicitly]
        private bool ScoreMaskValue {
            get => PluginConfig.LeaderboardTableMask.HasFlag(ScoreRowCellType.Score);
            set {
                if (value) {
                    PluginConfig.LeaderboardTableMask |= ScoreRowCellType.Score;
                } else {
                    PluginConfig.LeaderboardTableMask &= ~ScoreRowCellType.Score;
                }
            }
        }

        [UIValue("time-mask-value"), UsedImplicitly]
        private bool TimeMaskValue {
            get => PluginConfig.LeaderboardTableMask.HasFlag(ScoreRowCellType.Time);
            set {
                if (value) {
                    PluginConfig.LeaderboardTableMask |= ScoreRowCellType.Time;
                } else {
                    PluginConfig.LeaderboardTableMask &= ~ScoreRowCellType.Time;
                }
            }
        }

        [UIValue("mistakes-mask-value"), UsedImplicitly]
        private bool MistakesMaskValue {
            get => PluginConfig.LeaderboardTableMask.HasFlag(ScoreRowCellType.Mistakes);
            set {
                if (value) {
                    PluginConfig.LeaderboardTableMask |= ScoreRowCellType.Mistakes;
                } else {
                    PluginConfig.LeaderboardTableMask &= ~ScoreRowCellType.Mistakes;
                }
            }
        }

        #endregion
    }
}