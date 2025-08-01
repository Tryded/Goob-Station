// SPDX-FileCopyrightText: 2023 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Kara <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2023 Pieter-Jan Briers <pieterjan.briers@gmail.com>
// SPDX-FileCopyrightText: 2023 ShadowCommander <10494922+ShadowCommander@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 deltanedas <39013340+deltanedas@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 deltanedas <@deltanedas:kde.org>
// SPDX-FileCopyrightText: 2023 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 AJCM <AJCM@tutanota.com>
// SPDX-FileCopyrightText: 2024 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Piras314 <p1r4s@proton.me>
// SPDX-FileCopyrightText: 2024 Rainfall <rainfey0+git@gmail.com>
// SPDX-FileCopyrightText: 2024 Rainfey <rainfey0+github@gmail.com>
// SPDX-FileCopyrightText: 2024 Vasilis <vasilis@pikachu.systems>
// SPDX-FileCopyrightText: 2024 username <113782077+whateverusername0@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 whateverusername0 <whateveremail>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Errant <35878406+Errant-4@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 SX_7 <sn1.test.preria.2002@gmail.com>
// SPDX-FileCopyrightText: 2025 slarticodefast <161409025+slarticodefast@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Administration.Logs;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Roles;

public abstract class SharedRoleSystem : EntitySystem
{
    [Dependency] private   readonly IConfigurationManager _cfg = default!;
    [Dependency] private   readonly IEntityManager _entityManager = default!;
    [Dependency] private   readonly IPrototypeManager _prototypes = default!;
    [Dependency] private   readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] protected readonly ISharedPlayerManager Player = default!;
    [Dependency] private   readonly SharedAudioSystem _audio = default!;
    [Dependency] private   readonly SharedMindSystem _minds = default!;

    private JobRequirementOverridePrototype? _requirementOverride;

    public override void Initialize()
    {
        Subs.CVar(_cfg, CCVars.GameRoleTimerOverride, SetRequirementOverride, true);

        SubscribeLocalEvent<MindRoleComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<StartingMindRoleComponent, PlayerSpawnCompleteEvent>(OnSpawn);
    }

    private void OnSpawn(EntityUid uid, StartingMindRoleComponent component, PlayerSpawnCompleteEvent args)
    {
        if (!_minds.TryGetMind(uid, out var mindId, out var mindComp))
            return;

        MindAddRole(mindId, component.MindRole, mind: mindComp, silent: component.Silent);
    }

    private void SetRequirementOverride(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _requirementOverride = null;
            return;
        }

        if (!_prototypes.TryIndex(value, out _requirementOverride ))
            Log.Error($"Unknown JobRequirementOverridePrototype: {value}");
    }

    /// <summary>
    ///     Adds multiple mind roles to a mind
    /// </summary>
    /// <param name="mindId">The mind entity to add the role to</param>
    /// <param name="roles">The list of mind roles to add</param>
    /// <param name="mind">If the mind component is provided, it will be checked if it belongs to the mind entity</param>
    /// <param name="silent">If true, no briefing will be generated upon receiving the mind role</param>
    public void MindAddRoles(EntityUid mindId,
        List<EntProtoId>? roles,
        MindComponent? mind = null,
        bool silent = false)
    {
        if (roles is null || roles.Count == 0)
            return;

        foreach (var proto in roles)
        {
            MindAddRole(mindId, proto, mind, silent);
        }
    }

    /// <summary>
    ///     Adds a mind role to a mind
    /// </summary>
    /// <param name="mindId">The mind entity to add the role to</param>
    /// <param name="protoId">The mind role to add</param>
    /// <param name="mind">If the mind component is provided, it will be checked if it belongs to the mind entity</param>
    /// <param name="silent">If true, no briefing will be generated upon receiving the mind role</param>
    public void MindAddRole(EntityUid mindId,
        EntProtoId protoId,
        MindComponent? mind = null,
        bool silent = false)
    {
        if (protoId == "MindRoleJob")
            MindAddJobRole(mindId, mind, silent, "");
        else
            MindAddRoleDo(mindId, protoId, mind, silent);
    }

    /// <summary>
    /// Adds a Job mind role with the specified job prototype
    /// </summary>
    /// /// <param name="mindId">The mind entity to add the job role to</param>
    /// <param name="mind">If the mind component is provided, it will be checked if it belongs to the mind entity</param>
    /// <param name="silent">If true, no briefing will be generated upon receiving the mind role</param>
    /// <param name="jobPrototype">The Job prototype for the new role</param>
    public void MindAddJobRole(EntityUid mindId,
        MindComponent? mind = null,
        bool silent = false,
        string? jobPrototype = null)
    {
        if (!Resolve(mindId, ref mind))
            return;

        // Can't have someone get paid for two jobs now, can we
        if (MindHasRole<JobRoleComponent>((mindId, mind), out var jobRole)
            && jobRole.Value.Comp1.JobPrototype != jobPrototype)
        {
            _adminLogger.Add(LogType.Mind,
                LogImpact.Low,
                $"Job Role of {ToPrettyString(mind.OwnedEntity)} changed from '{jobRole.Value.Comp1.JobPrototype}' to '{jobPrototype}'");

            jobRole.Value.Comp1.JobPrototype = jobPrototype;
        }
        else
            MindAddRoleDo(mindId, "MindRoleJob", mind, silent, jobPrototype);
    }

    /// <summary>
    ///     Creates a Mind Role
    /// </summary>
    private void MindAddRoleDo(EntityUid mindId,
        EntProtoId protoId,
        MindComponent? mind = null,
        bool silent = false,
        string? jobPrototype = null)
    {
        if (!Resolve(mindId, ref mind))
        {
            Log.Error($"Failed to add role {protoId} to {ToPrettyString(mindId)} : Mind does not match provided mind component");
            return;
        }

        if (!_prototypes.TryIndex(protoId, out var protoEnt))
        {
            Log.Error($"Failed to add role {protoId} to {ToPrettyString(mindId)} : Role prototype does not exist");
            return;
        }

        //TODO don't let a prototype being added a second time
        //If that was somehow to occur, a second mindrole for that comp would be created
        //Meaning any mind role checks could return wrong results, since they just return the first match they find

        var mindRoleId = Spawn(protoId, MapCoordinates.Nullspace);
        EnsureComp<MindRoleComponent>(mindRoleId);
        var mindRoleComp = Comp<MindRoleComponent>(mindRoleId);

        mindRoleComp.Mind = (mindId,mind);
        if (jobPrototype is not null)
        {
            mindRoleComp.JobPrototype = jobPrototype;
            EnsureComp<JobRoleComponent>(mindRoleId);
            DebugTools.AssertNull(mindRoleComp.AntagPrototype);
            DebugTools.Assert(!mindRoleComp.Antag);
            DebugTools.Assert(!mindRoleComp.ExclusiveAntag);
        }

        mind.MindRoles.Add(mindRoleId);

        var update = MindRolesUpdate((mindId, mind));

        // RoleType refresh, Role time tracking, Update Admin playerlist

        var message = new RoleAddedEvent(mindId, mind, update, silent);
        RaiseLocalEvent(mindId, message, true);

        var name = Loc.GetString(protoEnt.Name);
        if (mind.OwnedEntity is not null)
        {
            _adminLogger.Add(LogType.Mind,
                LogImpact.Low,
                $"{name} added to mind of {ToPrettyString(mind.OwnedEntity)}");
        }
        else
        {
            //TODO: This is not tied to the player on the Admin Log filters.
            //Probably only happens when Job Role is added on initial spawn, before the mind entity is put in a mob
            Log.Error($"{ToPrettyString(mindId)} does not have an OwnedEntity!");
            _adminLogger.Add(LogType.Mind,
                LogImpact.Low,
                $"{name} added to {ToPrettyString(mindId)}");
        }
    }

    /// <summary>
    ///     Select the mind's currently "active" mind role entity, and update the mind's role type, if necessary
    /// </summary>
    /// <returns>
    ///     True if this changed the mind's role type
    /// </returns>>
    private bool MindRolesUpdate(Entity<MindComponent?> ent)
    {
        if(!Resolve(ent.Owner, ref ent.Comp))
            return false;

        //get the most important/latest mind role
        var (roleType, subtype) = GetRoleTypeByTime(ent.Comp);

        if (ent.Comp.RoleType == roleType &&  ent.Comp.Subtype == subtype)
            return false;

        SetRoleType(ent.Owner, roleType, subtype);
        return true;
    }

    /// <summary>
    ///     Return the most recently specified role type and subtype, or Neutral
    /// </summary>
    private (ProtoId<RoleTypePrototype>, LocId?) GetRoleTypeByTime(MindComponent mind)
    {
        var role = GetRoleCompByTime(mind);
        return (role?.Comp?.RoleType ?? "Neutral", role?.Comp?.Subtype);
    }

    /// <summary>
    ///     Return the most recently specified role type's mind role entity, or null
    /// </summary>
    public Entity<MindRoleComponent>? GetRoleCompByTime(MindComponent mind)
    {
        var roles = new List<Entity<MindRoleComponent>>();

        foreach (var role in mind.MindRoles)
        {
            var comp = Comp<MindRoleComponent>(role);
            if (comp.RoleType is not null)
                roles.Add((role, comp));
        }

        Entity<MindRoleComponent>? result = roles.Count > 0 ? roles.LastOrDefault() : null;
        return (result);
    }

    private void SetRoleType(EntityUid mind, ProtoId<RoleTypePrototype> roleTypeId, LocId? subtype)
    {
        if (!TryComp<MindComponent>(mind, out var comp))
        {
            Log.Error($"Failed to update Role Type of mind entity {ToPrettyString(mind)} to {roleTypeId}, {subtype}. MindComponent not found.");
            return;
        }

        if (!_prototypes.HasIndex(roleTypeId))
        {
            Log.Error($"Failed to change Role Type of {_minds.MindOwnerLoggingString(comp)} to {roleTypeId}, {subtype}. Invalid role");
            return;
        }

        comp.RoleType = roleTypeId;
        comp.Subtype = subtype;
        Dirty(mind, comp);

        // Update player character window
        if (Player.TryGetSessionById(comp.UserId, out var session))
            RaiseNetworkEvent(new MindRoleTypeChangedEvent(), session.Channel);
        else
        {
            var error = $"The Character Window of {_minds.MindOwnerLoggingString(comp)} potentially did not update immediately : session error";
            _adminLogger.Add(LogType.Mind, LogImpact.Medium, $"{error}");
        }

        if (comp.OwnedEntity is null)
        {
            Log.Error($"{ToPrettyString(mind)} does not have an OwnedEntity!");
            _adminLogger.Add(LogType.Mind,
                LogImpact.Medium,
                $"Role Type of {ToPrettyString(mind)} changed to {roleTypeId}, {subtype}");
            return;
        }

        _adminLogger.Add(LogType.Mind,
            LogImpact.High,
            $"Role Type of {ToPrettyString(comp.OwnedEntity)} changed to {roleTypeId}, {subtype}");
    }

    /// <summary>
    /// Finds and removes all mind roles of a specific type
    /// </summary>
    /// <param name="mind">The mind to remove the role from.</param>
    /// <typeparam name="T">The type of the role to remove.</typeparam>
    /// <returns>True if the role existed and was removed</returns>>
    public bool MindRemoveRole<T>(Entity<MindComponent?> mind) where T : IComponent
    {
        if (typeof(T) == typeof(MindRoleComponent))
            throw new InvalidOperationException();

        if (!Resolve(mind.Owner, ref mind.Comp))
            return false;

        var delete = new List<EntityUid>();
        // If there were no matches and thus no mind role entity names, we'll need the component's name, to report what role failed to be removed
        var original = "'" + typeof(T).Name + "'";
        var deleteName = original;

        foreach (var role in mind.Comp.MindRoles)
        {
            if (!HasComp<MindRoleComponent>(role))
            {
                Log.Error($"Encountered mind role entity {ToPrettyString(role)} without a {nameof(MindRoleComponent)}");
                continue;
            }

            if (!HasComp<T>(role))
                continue;

            delete.Add(role);
            deleteName = RemoveRoleLogNameGeneration(deleteName, MetaData(role).EntityName, original);
        }

        return MindRemoveRoleDo(mind, delete, deleteName);
    }

    private string RemoveRoleLogNameGeneration(string name, string newName, string original)
    {
        // If there were matches for deletion, this will run, and we get a new name to replace the original input
        if (name == original)
            name = "'" + newName + "'";
        // It is theoretically possible to get multiple matches
        // If they have different names, then we want all of them listed
        else if (!name.Contains(newName))
            // and we can't just drop the multiple names within a single ' ' section later, because that would
            // make it look like it's one name that is just formatted to look like a list
            name = name + ", " + "'" + newName + "'";

        return name;
    }

    /// <summary>
    /// Finds and removes all mind roles of a specific type
    /// </summary>
    /// <param name="mindId">The mind entity</param>
    /// <typeparam name="T">The type of the role to remove.</typeparam>
    /// <returns>True if the role existed and was removed</returns>
    public bool MindRemoveRole<T>(EntityUid mindId) where T : IComponent
    {
        if (!TryComp<MindComponent>(mindId, out var mind))
        {
            Log.Error($"The specified mind entity '{ToPrettyString(mindId)}' does not have a {nameof(MindComponent)}");
            return false;
        }

        return MindRemoveRole<T>((mindId, mind));
    }

        /// <summary>
    /// Finds and removes all mind roles of a specific type
    /// </summary>
    /// <param name="mind">The mind entity and component</param>
    /// /// <param name="protoId">The prototype ID of the mind role to be removed</param>
    /// <returns>True if the role existed and was removed</returns>
    public bool MindRemoveRole(Entity<MindComponent?> mind, EntProtoId<MindRoleComponent> protoId)
    {
        if ( !Resolve(mind.Owner, ref mind.Comp))
            return false;

        // If there were no matches and thus no mind role entity names, we'll need the protoId, to report what role failed to be removed
        var original = "'" + protoId + "'";
        var deleteName = original;
        var delete = new List<EntityUid>();
        foreach (var role in mind.Comp.MindRoles)
        {
            if (!HasComp<MindRoleComponent>(role))
            {
                Log.Error($"Encountered mind role entity {ToPrettyString(role)} without a {nameof(MindRoleComponent)}");
                continue;
            }

            var id = MetaData(role).EntityPrototype?.ID;
            if (id is null || id != protoId)
                continue;

            delete.Add(role);
            deleteName = RemoveRoleLogNameGeneration(deleteName, MetaData(role).EntityName, original);
        }

        return MindRemoveRoleDo(mind, delete, deleteName);
    }

    /// <summary>
    /// Performs the actual role entity deletion.
    /// </summary>
    private bool MindRemoveRoleDo(Entity<MindComponent?> mind, List<EntityUid> delete, string? logName = "")
    {
        if ( !Resolve(mind.Owner, ref mind.Comp))
            return false;

        if (delete.Count <= 0)
        {
            Log.Warning($"Failed to remove mind role {logName} from {ToPrettyString(mind.Owner)} : mind does not have this role ");
            return false;
        }

        foreach (var role in delete)
        {
            _entityManager.DeleteEntity(role);
        }

        var update = MindRolesUpdate(mind);

        var message = new RoleRemovedEvent(mind.Owner, mind.Comp, update);
        RaiseLocalEvent(mind, message, true);

        _adminLogger.Add(LogType.Mind,
            LogImpact.Low,
            $"All roles of type {logName} removed from mind of {ToPrettyString(mind.Comp.OwnedEntity)}");

        return true;
    }

    // Removing the mind role's reference on component shutdown
    // to make sure the reference gets removed even if the mind role entity was deleted by outside code
    private void OnComponentShutdown(Entity<MindRoleComponent> ent, ref ComponentShutdown args)
    {
        //TODO: Just ensure that the tests don't spawn unassociated mind role entities
        if (ent.Comp.Mind.Comp is null)
            return;

        ent.Comp.Mind.Comp.MindRoles.Remove(ent.Owner);
    }

    /// <summary>
    /// Finds the first mind role of a specific T type on a mind entity.
    /// Outputs entity components for the mind role's MindRoleComponent and for T
    /// </summary>
    /// <param name="mind">The mind entity</param>
    /// <typeparam name="T">The type of the role to find.</typeparam>
    /// <param name="role">The Mind Role entity component</param>
    /// <returns>True if the role is found</returns>
    public bool MindHasRole<T>(Entity<MindComponent?> mind,
        [NotNullWhen(true)] out Entity<MindRoleComponent, T>? role) where T : IComponent
    {
        role = null;
        if (!Resolve(mind.Owner, ref mind.Comp))
            return false;

        foreach (var roleEnt in mind.Comp.MindRoles)
        {
            if (!TryComp(roleEnt, out T? tcomp))
                continue;

            if (!TryComp(roleEnt, out MindRoleComponent? roleComp))
            {
                Log.Error($"Encountered mind role entity {ToPrettyString(roleEnt)} without a {nameof(MindRoleComponent)}");
                continue;
            }
            role = (roleEnt, roleComp, tcomp);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the first mind role of a specific type on a mind entity.
    /// Outputs an entity component for the mind role's MindRoleComponent
    /// </summary>
    /// <param name="mindId">The mind entity</param>
    /// <param name="type">The Type to look for</param>
    /// <param name="role">The output role</param>
    /// <returns>True if the role is found</returns>
    public bool MindHasRole(EntityUid mindId,
        Type type,
        [NotNullWhen(true)] out Entity<MindRoleComponent>? role)
    {
        role = null;
        // All MindRoles have this component, it would just return the first one.
        // Order might not be what is expected.
        // Better to report null
        if (type == Type.GetType("MindRoleComponent"))
        {
            Log.Error($"Something attempted to query mind role 'MindRoleComponent' on mind {mindId}. This component is present on every single mind role.");
            return false;
        }

        if (!TryComp<MindComponent>(mindId, out var mind))
            return false;

        var found = false;

        foreach (var roleEnt in mind.MindRoles)
        {
            if (!HasComp(roleEnt, type))
                continue;

            if (!TryComp(roleEnt, out MindRoleComponent? roleComp))
            {
                Log.Error($"Encountered mind role entity {ToPrettyString(roleEnt)} without a {nameof(MindRoleComponent)}");
                continue;
            }

            role = (roleEnt, roleComp);
            found = true;
            break;
        }

        return found;
    }

    /// <summary>
    /// Finds the first mind role of a specific type on a mind entity.
    /// </summary>
    /// <param name="mindId">The mind entity</param>
    /// <typeparam name="T">The type of the role to find.</typeparam>
    /// <returns>True if the role is found</returns>
    public bool MindHasRole<T>(EntityUid mindId) where T : IComponent
    {
        return MindHasRole<T>(mindId, out _);
    }

    //TODO: Delete this later
    /// <summary>
    /// Returns the first mind role of a specific type
    /// </summary>
    /// <param name="mindId">The mind entity</param>
    /// <returns>Entity Component of the mind role</returns>
    [Obsolete("Use MindHasRole's output value")]
    public Entity<MindRoleComponent>? MindGetRole<T>(EntityUid mindId) where T : IComponent
    {
        Entity<MindRoleComponent>? result = null;

        var mind = Comp<MindComponent>(mindId);

        foreach (var uid in mind.MindRoles)
        {
            if (HasComp<T>(uid) && TryComp<MindRoleComponent>(uid, out var comp))
                result = (uid,comp);
        }
        return result;
    }

    /// <summary>
    /// Reads all Roles of a mind Entity and returns their data as RoleInfo
    /// </summary>
    /// <param name="mind">The mind entity</param>
    /// <returns>RoleInfo list</returns>
    public List<RoleInfo> MindGetAllRoleInfo(Entity<MindComponent?> mind)
    {
        var roleInfo = new List<RoleInfo>();

        if (!Resolve(mind.Owner, ref mind.Comp))
            return roleInfo;

        foreach (var role in mind.Comp.MindRoles)
        {
            var valid = false;
            var name = "game-ticker-unknown-role";
            var prototype = "";
            string? playTimeTracker = null;

            if (!TryComp(role, out MindRoleComponent? comp))
            {
                Log.Error($"Encountered mind role entity {ToPrettyString(role)} without a {nameof(MindRoleComponent)}");
                continue;
            }

            if (comp.AntagPrototype is not null)
                prototype = comp.AntagPrototype;

            if (comp.JobPrototype is not null && comp.AntagPrototype is null)
            {
                prototype = comp.JobPrototype;
                if (_prototypes.TryIndex(comp.JobPrototype, out var job))
                {
                    playTimeTracker = job.PlayTimeTracker;
                    name = job.Name;
                    valid = true;
                }
                else
                {
                    Log.Error($" Mind Role Prototype '{role.Id}' contains invalid Job prototype: '{comp.JobPrototype}'");
                }
            }
            else if (comp.AntagPrototype is not null && comp.JobPrototype is null)
            {
                prototype = comp.AntagPrototype;
                if (_prototypes.TryIndex(comp.AntagPrototype, out var antag))
                {
                    name = antag.Name;
                    valid = true;
                }
                else
                {
                    Log.Error($" Mind Role Prototype '{role.Id}' contains invalid Antagonist prototype: '{comp.AntagPrototype}'");
                }
            }
            else if (comp.JobPrototype is not null && comp.AntagPrototype is not null)
            {
                Log.Error($" Mind Role Prototype '{role.Id}' contains both Job and Antagonist prototypes");
            }

            if (valid)
                roleInfo.Add(new RoleInfo(name, comp.Antag, playTimeTracker, prototype));
        }
        return roleInfo;
    }

    /// <summary>
    /// Does this mind possess an antagonist role
    /// </summary>
    /// <param name="mindId">The mind entity</param>
    /// <returns>True if the mind possesses any antag roles</returns>
    public bool MindIsAntagonist(EntityUid? mindId)
    {
        if (mindId is null)
            return false;

        return CheckAntagonistStatus(mindId.Value).Antag;
    }

    /// <summary>
    /// Does this mind possess an exclusive antagonist role
    /// </summary>
    /// <param name="mindId">The mind entity</param>
    /// <returns>True if the mind possesses any exclusive antag roles</returns>
    public bool MindIsExclusiveAntagonist(EntityUid? mindId)
    {
        if (mindId is null)
            return false;

        return CheckAntagonistStatus(mindId.Value).ExclusiveAntag;
    }

   private (bool Antag, bool ExclusiveAntag) CheckAntagonistStatus(Entity<MindComponent?> mind)
   {
       if (!Resolve(mind.Owner, ref mind.Comp))
           return (false, false);

        var antagonist = false;
        var exclusiveAntag = false;
        foreach (var role in mind.Comp.MindRoles)
        {
            if (!TryComp<MindRoleComponent>(role, out var roleComp))
            {
                Log.Error($"Mind Role Entity {ToPrettyString(role)} does not have a MindRoleComponent, despite being listed as a role belonging to {ToPrettyString(mind)}|");
                continue;
            }

            antagonist |= roleComp.Antag;
            exclusiveAntag |= roleComp.ExclusiveAntag;
        }

        return (antagonist, exclusiveAntag);
    }

    /// <summary>
    /// Play a sound for the mind, if it has a session attached.
    /// Use this for role greeting sounds.
    /// </summary>
    public void MindPlaySound(EntityUid mindId, SoundSpecifier? sound, MindComponent? mind = null)
    {
        if (!Resolve(mindId, ref mind))
            return;

        if (Player.TryGetSessionById(mind.UserId, out var session))
            _audio.PlayGlobal(sound, session);
    }

    // TODO ROLES Change to readonly.
    // Passing around a reference to a prototype's hashset makes me uncomfortable because it might be accidentally
    // mutated.
    public HashSet<JobRequirement>? GetJobRequirement(JobPrototype job)
    {
        if (_requirementOverride != null && _requirementOverride.Jobs.TryGetValue(job.ID, out var req))
            return req;

        return job.Requirements;
    }

    // TODO ROLES Change to readonly.
    public HashSet<JobRequirement>? GetJobRequirement(ProtoId<JobPrototype> job)
    {
        if (_requirementOverride != null && _requirementOverride.Jobs.TryGetValue(job, out var req))
            return req;

        return _prototypes.Index(job).Requirements;
    }

    // TODO ROLES Change to readonly.
    public HashSet<JobRequirement>? GetAntagRequirement(ProtoId<AntagPrototype> antag)
    {
        if (_requirementOverride != null && _requirementOverride.Antags.TryGetValue(antag, out var req))
            return req;

        return _prototypes.Index(antag).Requirements;
    }

    // TODO ROLES Change to readonly.
    public HashSet<JobRequirement>? GetAntagRequirement(AntagPrototype antag)
    {
        if (_requirementOverride != null && _requirementOverride.Antags.TryGetValue(antag.ID, out var req))
            return req;

        return antag.Requirements;
    }

    /// <summary>
    /// Returns the localized name of a role type's subtype. If the provided subtype parameter turns out to be empty, it returns the localized name of the role type instead.
    /// </summary>
    public string GetRoleSubtypeLabel(LocId roleType, LocId? subtype)
    {
        return string.IsNullOrEmpty(subtype) ? Loc.GetString(roleType) : Loc.GetString(subtype);
    }
}

/// <summary>
/// Raised on the client to update Role Type on the character window, in case it happened to be open.
/// </summary>
[Serializable, NetSerializable]
public sealed class MindRoleTypeChangedEvent : EntityEventArgs
{

}
