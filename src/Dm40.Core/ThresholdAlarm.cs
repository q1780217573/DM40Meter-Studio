using System;

namespace Dm40.Core
{
    /// <summary>
    /// 阈值报警（PRD FR-007、§5.2 规则4）。
    /// 判定规则：
    ///  - 越限 = 工程值 &gt; 上限 或 &lt; 下限（严格不等）；留空方向不判定。
    ///  - 0.5s 滞回：越限需持续 ≥ 0.5s 才触发；期间回落则取消计时。
    ///  - 同方向越限在恢复前不重复触发（触发后闭锁，回到限内复位）。
    ///  - 上限必须大于下限（<see cref="Validate"/> 校验）。
    /// 过载/无效帧（Feed(null)）不参与判定。时间经 <see cref="IClock"/> 注入，便于测试滞回。
    /// </summary>
    public sealed class ThresholdAlarm
    {
        /// <summary>滞回时长（PRD 固定 0.5s）。</summary>
        public static readonly TimeSpan Hysteresis = TimeSpan.FromMilliseconds(500);

        private readonly IClock _clock;

        // 滞回计时状态：当前越限方向、越限起始时刻
        private AlarmDirection? _pendingDirection;
        private DateTime _overSince;
        // 触发闭锁：已触发且尚未恢复的方向
        private bool _highLatched;
        private bool _lowLatched;

        public ThresholdAlarm(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>是否启用报警。</summary>
        public bool IsEnabled { get; set; }

        /// <summary>上限（留空 null = 该方向不判定）。</summary>
        public double? UpperLimit { get; set; }

        /// <summary>下限（留空 null = 该方向不判定）。</summary>
        public double? LowerLimit { get; set; }

        /// <summary>触发事件（声音/弹窗/红标由上层订阅）。</summary>
        public event Action<AlarmEvent> Triggered;

        /// <summary>校验阈值合法性：上限必须大于下限（二者都设置时）。保存配置时调用。</summary>
        public void Validate()
        {
            if (UpperLimit.HasValue && LowerLimit.HasValue && UpperLimit.Value <= LowerLimit.Value)
                throw new InvalidOperationException("上限必须大于下限。");
        }

        /// <summary>
        /// 喂入一个有效工程值。过载/无效帧传 null（不参与判定）。
        /// </summary>
        public void Feed(double? value)
        {
            if (!IsEnabled || !value.HasValue)
                return;

            double v = value.Value;
            bool overHigh = UpperLimit.HasValue && v > UpperLimit.Value;
            bool overLow = LowerLimit.HasValue && v < LowerLimit.Value;

            // 恢复检测：回到限内 → 复位对应方向的闭锁与计时
            if (!overHigh) { _highLatched = false; if (_pendingDirection == AlarmDirection.High) _pendingDirection = null; }
            if (!overLow) { _lowLatched = false; if (_pendingDirection == AlarmDirection.Low) _pendingDirection = null; }

            // 当前越限方向（理论上 high 与 low 不会同时成立，因 upper>lower）
            AlarmDirection? current = overHigh ? AlarmDirection.High : (overLow ? AlarmDirection.Low : (AlarmDirection?)null);
            if (current == null)
                return;

            // 已开始越限计时？
            if (_pendingDirection != current)
            {
                _pendingDirection = current;
                _overSince = _clock.UtcNow;
                return;
            }

            // 越限持续中 → 滞回计时
            if (_clock.UtcNow - _overSince < Hysteresis)
                return;

            // 超过滞回：若该方向未闭锁则触发并闭锁
            bool latched = current == AlarmDirection.High ? _highLatched : _lowLatched;
            if (latched)
                return;

            if (current == AlarmDirection.High) _highLatched = true; else _lowLatched = true;
            Triggered?.Invoke(new AlarmEvent(_clock.UtcNow, current.Value, v));
        }
    }
}
