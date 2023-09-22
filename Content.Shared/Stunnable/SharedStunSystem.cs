using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Audio;
using Content.Shared.DragDrop;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Bed.Sleep;
using Content.Shared.Database;
using Content.Shared.Hands;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Content.Shared.StatusEffects;
using Content.Shared.StatusEffects.Components;
using Robust.Shared.Timing;

namespace Content.Shared.Stunnable
{
    public abstract class SharedStunSystem : EntitySystem
    {
        [Dependency] private readonly ActionBlockerSystem _blocker = default!;
        [Dependency] private readonly StandingStateSystem _standingStateSystem = default!;
        [Dependency] private readonly SharedStatusEffectsSystem _statusEffectSystem = default!;
        [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;
        [Dependency] private readonly SharedAudioSystem _audio = default!;
        [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;

        /// <summary>
        /// Friction modifier for knocked down players.
        /// Doesn't make them faster but makes them slow down... slower.
        /// </summary>
        public const float KnockDownModifier = 0.4f;

        public override void Initialize()
        {
            SubscribeLocalEvent<KnockedDownComponent, ComponentInit>(OnKnockInit);
            SubscribeLocalEvent<KnockedDownComponent, ComponentShutdown>(OnKnockShutdown);
            SubscribeLocalEvent<KnockedDownComponent, StatusEffectRelayEvent<StandAttemptEvent>>(OnStandAttempt);
            SubscribeLocalEvent<KnockedDownComponent, ComponentGetState>(OnKnockGetState);
            SubscribeLocalEvent<KnockedDownComponent, ComponentHandleState>(OnKnockHandleState);
            SubscribeLocalEvent<KnockedDownComponent, StatusEffectRelayEvent<InteractHandEvent>>(OnInteractHand);
            SubscribeLocalEvent<KnockedDownComponent, StatusEffectRelayEvent<TileFrictionEvent>>(OnKnockedTileFriction);

            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<ComponentStartup>>(UpdateCanMove);
            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<ComponentShutdown>>(UpdateCanMove);
            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<ChangeDirectionAttemptEvent>>(OnAttempt);
            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<UpdateCanMoveEvent>>(OnMoveAttempt);
            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<InteractionAttemptEvent>>(OnAttempt);
            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<UseAttemptEvent>>(OnAttempt);
            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<ThrowAttemptEvent>>(OnAttempt);
            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<DropAttemptEvent>>(OnAttempt);
            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<AttackAttemptEvent>>(OnAttempt);
            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<PickupAttemptEvent>>(OnAttempt);
            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<IsEquippingAttemptEvent>>(OnEquipAttempt);
            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<IsUnequippingAttemptEvent>>(OnUnequipAttempt);
            SubscribeLocalEvent<StunnedComponent, StatusEffectRelayEvent<MobStateChangedEvent>>(OnMobStateChanged);

            SubscribeLocalEvent<SlowedDownComponent, StatusEffectRelayEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshMovespeed);
            SubscribeLocalEvent<SlowedDownComponent, ComponentInit>(OnSlowInit);
            SubscribeLocalEvent<SlowedDownComponent, ComponentShutdown>(OnSlowRemove);
            SubscribeLocalEvent<SlowedDownComponent, ComponentGetState>(OnSlowGetState);
            SubscribeLocalEvent<SlowedDownComponent, ComponentHandleState>(OnSlowHandleState);
        }



        private void OnMobStateChanged(EntityUid uid, StunnedComponent component, StatusEffectRelayEvent<MobStateChangedEvent> args)
        {
            switch (args.NewMobState)
            {
                case MobState.Alive:
                    break;
                case MobState.Critical:
                    _statusEffectSystem.ModifyEffect(uid, 0);
                    break;
                case MobState.Dead:
                    _statusEffectSystem.ModifyEffect(uid, 0);
                    break;
                case MobState.Invalid:
                default:
                    return;
            }

        }

        private void UpdateCanMove(EntityUid uid, StunnedComponent component, EntityEventArgs args)
        {
            _blocker.UpdateCanMove(uid);
        }

        private void OnSlowGetState(EntityUid uid, SlowedDownComponent component, ref ComponentGetState args)
        {
            args.State = new SlowedDownComponentState(component.SprintSpeedModifier, component.WalkSpeedModifier);
        }

        private void OnSlowHandleState(EntityUid uid, SlowedDownComponent component, ref ComponentHandleState args)
        {
            if (args.Current is SlowedDownComponentState state)
            {
                component.SprintSpeedModifier = state.SprintSpeedModifier;
                component.WalkSpeedModifier = state.WalkSpeedModifier;
            }
        }

        private void OnKnockGetState(EntityUid uid, KnockedDownComponent component, ref ComponentGetState args)
        {
            args.State = new KnockedDownComponentState(component.HelpInterval, component.HelpTimer);
        }

        private void OnKnockHandleState(EntityUid uid, KnockedDownComponent component, ref ComponentHandleState args)
        {
            if (args.Current is KnockedDownComponentState state)
            {
                component.HelpInterval = state.HelpInterval;
                component.HelpTimer = state.HelpTimer;
            }
        }

        private void OnKnockInit(EntityUid uid, KnockedDownComponent component, ComponentInit args)
        {
            _standingStateSystem.Down(uid);
        }

        private void OnKnockShutdown(EntityUid uid, KnockedDownComponent component, ComponentShutdown args)
        {
            _standingStateSystem.Stand(uid);
        }

        private void OnStandAttempt(EntityUid uid, KnockedDownComponent component, StatusEffectRelayEvent<StandAttemptEvent> args)
        {
            if (component.LifeStage <= ComponentLifeStage.Running)
                args.Args.Cancel();
        }

        private void OnSlowInit(EntityUid uid, SlowedDownComponent component, ComponentInit args)
        {
            _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
        }

        private void OnSlowRemove(EntityUid uid, SlowedDownComponent component, ComponentShutdown args)
        {
            component.SprintSpeedModifier = 1f;
            component.WalkSpeedModifier = 1f;
            _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
        }

        private void OnRefreshMovespeed(EntityUid uid, SlowedDownComponent component, StatusEffectRelayEvent<RefreshMovementSpeedModifiersEvent> args)
        {
            args.Args.ModifySpeed(component.WalkSpeedModifier, component.SprintSpeedModifier);
        }

        // TODO STUN: Make events for different things. (Getting modifiers, attempt events, informative events...)

        // TODO: Admin logger for status effects overall.
        // _adminLogger.Add(LogType.Stamina, LogImpact.Medium, $"{ToPrettyString(uid):user} stunned for {time.Seconds} seconds");

        /// <summary>
        ///     Applies knockdown and stun to the entity temporarily.
        /// </summary>
        public bool TryParalyze(EntityUid uid, TimeSpan time, StatusEffectsComponent? status = null)
        {
            if (!Resolve(uid, ref status, false))
                return false;

            return TryKnockdown(uid, time, status) && TryStun(uid, time, status);
        }

        /// <summary>
        ///     Slows down the mob's walking/running speed temporarily
        /// </summary>
        public bool TrySlowdown(EntityUid uid, TimeSpan time, bool refresh,
            float walkSpeedMultiplier = 1f, float runSpeedMultiplier = 1f,
            StatusEffectsComponent? status = null)
        {
            if (!Resolve(uid, ref status, false))
                return false;

            if (time <= TimeSpan.Zero)
                return false;

            if (_statusEffectSystem.TryApplyStatusEffect(uid, "SlowedDownEffect", out var slowEffect, 1, time, comp: status))
            {
                var slowed = EntityManager.GetComponent<SlowedDownComponent>(slowEffect!.Value);
                // Doesn't make much sense to have the "TrySlowdown" method speed up entities now does it?
                walkSpeedMultiplier = Math.Clamp(walkSpeedMultiplier, 0f, 1f);
                runSpeedMultiplier = Math.Clamp(runSpeedMultiplier, 0f, 1f);

                slowed.WalkSpeedModifier *= walkSpeedMultiplier;
                slowed.SprintSpeedModifier *= runSpeedMultiplier;

                _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);

                return true;
            }

            return false;
        }

        private void OnInteractHand(EntityUid uid, KnockedDownComponent knocked, StatusEffectRelayEvent<InteractHandEvent> args)
        {
            if (args.Args.Handled || knocked.HelpTimer > 0f)
                return;

            // TODO: This should be an event.
            if (HasComp<SleepingComponent>(uid))
                return;

            // Set it to half the help interval so helping is actually useful...
            knocked.HelpTimer = knocked.HelpInterval / 2f;

            _statusEffectSystem.ModifyEffect(uid, length: TimeSpan.FromSeconds(-knocked.HelpInterval), effectApplyType: EffectModifyMode.AddTime);
            _audio.PlayPredicted(knocked.StunAttemptSound, uid, args.Afflicted);
            Dirty(knocked);

            args.Args.Handled = true;
        }

        private void OnKnockedTileFriction(EntityUid uid, KnockedDownComponent component, StatusEffectRelayEvent<TileFrictionEvent> args)
        {
            args.Args.Modifier *= KnockDownModifier;
        }

        #region Attempt Event Handling

        private void OnMoveAttempt(EntityUid uid, StunnedComponent stunned, StatusEffectRelayEvent<UpdateCanMoveEvent> args)
        {
            if (stunned.LifeStage > ComponentLifeStage.Running)
                return;

            args.Args.Cancel();
        }

        private void OnAttempt<TEvent>(EntityUid uid, StunnedComponent stunned, StatusEffectRelayEvent<TEvent> args) where TEvent : CancellableEntityEventArgs
        {
            args.Args.Cancel();
        }

        private void OnEquipAttempt(EntityUid uid, StunnedComponent stunned, StatusEffectRelayEvent<IsEquippingAttemptEvent> args)
        {
            // is this a self-equip, or are they being stripped?
            if (args.Args.Equipee == args.Afflicted)
                args.Args.Cancel();
        }

        private void OnUnequipAttempt(EntityUid uid, StunnedComponent stunned, StatusEffectRelayEvent<IsUnequippingAttemptEvent> args)
        {
            // is this a self-unequip, or are they being stripped?
            if (args.Args.Unequipee == args.Afflicted)
                args.Args.Cancel();
        }

        #endregion

    }
}
