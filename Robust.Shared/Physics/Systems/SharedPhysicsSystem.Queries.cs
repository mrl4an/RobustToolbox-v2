using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Collections;
using Robust.Shared.Debugging;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Physics.Collision;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Utility;

namespace Robust.Shared.Physics.Systems
{
    /*
     * Handles all of the public query methods for physics.
     */
    public partial class SharedPhysicsSystem
    {
#if DEBUG
        [Dependency] private readonly SharedDebugRayDrawingSystem _sharedDebugRaySystem = default!;
        [Dependency] private readonly INetManager _netMan = default!;
#endif

        /// <summary>
        ///     Checks whether the given world-space rectangle overlaps any managed physics body.
        /// </summary>
        /// <param name="collider">The rectangle to test, in world space.</param>
        /// <param name="mapId">The map the rectangle lives on.</param>
        /// <param name="approximate">Whether the broadphase may use loose (approximate) bounds.</param>
        /// <returns>True when at least one fixture proxy with a non-empty collision layer overlaps.</returns>
        public bool TryCollideRect(Box2 collider, MapId mapId, bool approximate = true)
        {
            var state = (collider, mapId, found: false);

            _broadphase.GetBroadphases(mapId, collider, (Entity<BroadphaseComponent> broadphase) =>
            {
                // Bring the rectangle into the local space of this broadphase.
                var gridCollider = _transform.GetInvWorldMatrix(broadphase).TransformBox(collider);

                // The same per-proxy test is used for both trees.
                static bool Visit(ref (Box2 collider, MapId mapId, bool found) s, in FixtureProxy proxy, Box2 local)
                {
                    if (proxy.Fixture.CollisionLayer == 0)
                        return true;

                    if (proxy.AABB.Intersects(local))
                    {
                        s.found = true;
                        return false;
                    }

                    return true;
                }

                broadphase.Comp.StaticTree.QueryAabb(ref state,
                    (ref (Box2 collider, MapId mapId, bool found) s, in FixtureProxy proxy) =>
                        Visit(ref s, in proxy, gridCollider),
                    gridCollider, approximate);

                broadphase.Comp.DynamicTree.QueryAabb(ref state,
                    (ref (Box2 collider, MapId mapId, bool found) s, in FixtureProxy proxy) =>
                        Visit(ref s, in proxy, gridCollider),
                    gridCollider, approximate);
            });

            return state.found;
        }

        /// <summary>
        ///     Get all the entities whose fixtures intersect the fixtures of the given entity. Basically a variant of
        ///     <see cref="GetCollidingEntities(PhysicsComponent, Vector2, bool)"/> that allows the user to specify
        ///     their own collision mask.
        /// </summary>
        public HashSet<EntityUid> GetEntitiesIntersectingBody(
            EntityUid uid,
            int collisionMask,
            bool approximate = true,
            PhysicsComponent? body = null,
            FixturesComponent? fixtureComp = null,
            TransformComponent? xform = null)
        {
            var entities = new HashSet<EntityUid>();

            if (!Resolve(uid, ref body, ref fixtureComp, ref xform, false))
                return entities;

            if (!_lookup.TryGetCurrentBroadphase(xform, out var broadphase))
                return entities;

            var state = (body, entities);

            foreach (var fixture in fixtureComp.Fixtures.Values)
            {
                foreach (var proxy in fixture.Proxies)
                {
                    broadphase.StaticTree.QueryAabb(ref state,
                        (ref (PhysicsComponent body, HashSet<EntityUid> entities) state,
                            in FixtureProxy other) =>
                        {
                            if (other.Body.Deleted || other.Body == body) return true;
                            if ((collisionMask & other.Fixture.CollisionLayer) == 0x0) return true;

                            state.entities.Add(other.Entity);
                            return true;
                        }, proxy.AABB, approximate);

                    broadphase.DynamicTree.QueryAabb(ref state,
                        (ref (PhysicsComponent body, HashSet<EntityUid> entities) state,
                            in FixtureProxy other) =>
                        {
                            if (other.Body.Deleted || other.Body == body) return true;
                            if ((collisionMask & other.Fixture.CollisionLayer) == 0x0) return true;

                            state.entities.Add(other.Entity);
                            return true;
                        }, proxy.AABB, approximate);
                }
            }

            return entities;
        }

        /// <summary>
        /// Get all entities colliding with a certain body.
        /// </summary>
        [Obsolete("Use EntityLookupSystem")]
        public IEnumerable<PhysicsComponent> GetCollidingEntities(MapId mapId, in Box2 worldAABB)
        {
            if (mapId == MapId.Nullspace) return Array.Empty<PhysicsComponent>();

            var aabb = worldAABB;
            var bodies = new HashSet<PhysicsComponent>();
            var state = (_transform, bodies, aabb);

            _broadphase.GetBroadphases(mapId, worldAABB, ref state, static
                (
                    Entity<BroadphaseComponent> entity,
                    ref (SharedTransformSystem _transform, HashSet<PhysicsComponent> bodies, Box2 aabb) tuple) =>
                {
                    var gridAABB = tuple._transform.GetInvWorldMatrix(entity.Owner).TransformBox(tuple.aabb);

                    foreach (var proxy in entity.Comp.StaticTree.QueryAabb(gridAABB, false))
                    {
                        tuple.bodies.Add(proxy.Body);
                    }

                    foreach (var proxy in entity.Comp.DynamicTree.QueryAabb(gridAABB, false))
                    {
                        tuple.bodies.Add(proxy.Body);
                    }
                });

            return bodies;
        }

        /// <summary>
        /// Get all entities colliding with a certain body.
        /// </summary>
        [Obsolete("Use EntityLookupSystem")]
        public IEnumerable<Entity<PhysicsComponent>> GetCollidingEntities(MapId mapId, in Box2Rotated worldBounds)
        {
            if (mapId == MapId.Nullspace)
                return Array.Empty<Entity<PhysicsComponent>>();

            var bodies = new HashSet<Entity<PhysicsComponent>>();

            var state = (_transform, bodies, worldBounds);

            _broadphase.GetBroadphases(mapId, worldBounds.CalcBoundingBox(), ref state,
                static (
                    Entity<BroadphaseComponent> entity,
                    ref (SharedTransformSystem _transform, HashSet<Entity<PhysicsComponent>> bodies, Box2Rotated
                        worldBounds
                        ) tuple) =>
                {
                    var gridAABB = tuple._transform.GetInvWorldMatrix(entity.Owner).TransformBox(tuple.worldBounds);

                    foreach (var proxy in entity.Comp.StaticTree.QueryAabb(gridAABB, false))
                    {
                        tuple.bodies.Add((proxy.Entity, proxy.Body));
                    }

                    foreach (var proxy in entity.Comp.DynamicTree.QueryAabb(gridAABB, false))
                    {
                        tuple.bodies.Add((proxy.Entity, proxy.Body));
                    }
                });

            return bodies;
        }

        public void GetContactingEntities(Entity<PhysicsComponent?> ent, HashSet<EntityUid> contacting, bool approximate = false)
        {
            if (!Resolve(ent.Owner, ref ent.Comp))
                return;

            var node = ent.Comp.Contacts.First;

            while (node != null)
            {
                var contact = node.Value;
                node = node.Next;

                if (approximate || contact.IsTouching)
                    contacting.Add(ent.Owner == contact.EntityA ? contact.EntityB : contact.EntityA);
            }
        }

        public HashSet<EntityUid> GetContactingEntities(EntityUid uid, PhysicsComponent? body = null, bool approximate = false)
        {
            var result = new HashSet<EntityUid>();
            GetContactingEntities((uid, body), result, approximate);
            return result;
        }

        /// <summary>
        ///     Checks whether a body is colliding
        /// </summary>
        public bool IsInContact(PhysicsComponent body, bool approximate = false)
        {
            var node = body.Contacts.First;

            while (node != null)
            {
                if (approximate || node.Value.IsTouching)
                    return true;

                node = node.Next;
            }

            return false;
        }

        #region RayCast

        /// <summary>
        ///     Casts a ray in the world, returning the first entity it hits (or all entities it hits, if so specified)
        /// </summary>
        /// <param name="mapId"></param>
        /// <param name="ray">The ray to cast through the world.</param>
        /// <param name="maxLength">How far the ray reaches, in meters.</param>
        /// <param name="predicate">A predicate to check whether to ignore an entity or not. If it returns true, it will be ignored.</param>
        /// <param name="returnOnFirstHit">If true, will only include the first hit entity in results. Otherwise, returns all of them.</param>
        /// <returns>A result object describing the hit, if any.</returns>
        // TODO: Make the parameter order here consistent with the other overload.
        public IEnumerable<RayCastResults> IntersectRayWithPredicate(MapId mapId, CollisionRay ray,
            float maxLength = 50F, Func<EntityUid, bool>? predicate = null, bool returnOnFirstHit = true)
        {
            // No, rider. This is better than a local function!
            // ReSharper disable once ConvertToLocalFunction
            var wrapper =
                (EntityUid uid, Func<EntityUid, bool>? wrapped)
                    => wrapped != null && wrapped(uid);

            return IntersectRayWithPredicate(mapId, ray, predicate, wrapper, maxLength, returnOnFirstHit);
        }

        /// <summary>
        ///     Casts a ray through the world and returns either the first hit or every hit.
        /// </summary>
        /// <typeparam name="TState">Type of the caller-supplied state; passing state avoids closure allocations.</typeparam>
        /// <param name="mapId">The map to cast on.</param>
        /// <param name="ray">The ray to cast.</param>
        /// <param name="state">State handed to <paramref name="predicate"/> for each candidate entity.</param>
        /// <param name="predicate">Returns true for entities that must be ignored.</param>
        /// <param name="maxLength">Maximum ray length in meters.</param>
        /// <param name="returnOnFirstHit">If true only one hit is reported; otherwise all hits are.</param>
        /// <returns>The hits, sorted by increasing distance.</returns>
        public IEnumerable<RayCastResults> IntersectRayWithPredicate<TState>(
            MapId mapId,
            CollisionRay ray,
            TState state,
            Func<EntityUid, TState, bool> predicate,
            float maxLength = 50F,
            bool returnOnFirstHit = true)
        {
            var results = new List<RayCastResults>();
            var endPoint = ray.Position + ray.Direction.Normalized() * maxLength;
            var rayBox = new Box2(Vector2.Min(ray.Position, endPoint), Vector2.Max(ray.Position, endPoint));

            _broadphase.GetBroadphases(mapId, rayBox, (Entity<BroadphaseComponent> broadphase) =>
            {
                var (_, rot, matrix, invMatrix) = _transform.GetWorldPositionRotationMatrixWithInv(broadphase.Owner);

                // Express the ray in the local frame of the broadphase.
                var localPosition = Vector2.Transform(ray.Position, invMatrix);
                var gridRot = new Angle(-rot.Theta);
                var localDirection = gridRot.RotateVec(ray.Direction);
                var gridRay = new CollisionRay(localPosition, localDirection, ray.CollisionMask);

                bool Visit(in FixtureProxy proxy, in Vector2 point, float distFromOrigin)
                {
                    if (returnOnFirstHit && results.Count > 0)
                        return true;

                    if (distFromOrigin > maxLength)
                        return true;

                    if ((proxy.Fixture.CollisionLayer & ray.CollisionMask) == 0)
                        return true;

                    if (!proxy.Fixture.Hard)
                        return true;

                    if (predicate(proxy.Entity, state))
                        return true;

                    var worldHit = Vector2.Transform(point, matrix);
                    var result = new RayCastResults(distFromOrigin, worldHit, proxy.Entity);
                    results.Add(result);
#if DEBUG
                    _sharedDebugRaySystem.ReceiveLocalRayFromAnyThread(
                        new(ray, maxLength, result, _netMan.IsServer, mapId));
#endif
                    return true;
                }

                broadphase.Comp.StaticTree.QueryRay(Visit, gridRay);
                broadphase.Comp.DynamicTree.QueryRay(Visit, gridRay);
            });

#if DEBUG
            if (results.Count == 0)
            {
                _sharedDebugRaySystem.ReceiveLocalRayFromAnyThread(
                    new(ray, maxLength, null, _netMan.IsServer, mapId));
            }
#endif

            results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return results;
        }

        /// <summary>
        ///     Casts a ray through the world, optionally skipping a single entity.
        /// </summary>
        /// <param name="mapId">The map to cast on.</param>
        /// <param name="ray">The ray to cast.</param>
        /// <param name="maxLength">Maximum ray length in meters.</param>
        /// <param name="ignoredEnt">An entity to ignore; null ignores nothing.</param>
        /// <param name="returnOnFirstHit">If true only one hit is reported; otherwise all hits are.</param>
        /// <returns>The hits, sorted by increasing distance.</returns>
        public IEnumerable<RayCastResults> IntersectRay(
            MapId mapId,
            CollisionRay ray,
            float maxLength = 50,
            EntityUid? ignoredEnt = null,
            bool returnOnFirstHit = true)
        {
            // Explicitly typed so the generic overload (not the Func<EntityUid, bool> one) is chosen.
            var wrapper = static (EntityUid uid, EntityUid? ignored) => uid == ignored;

            return IntersectRayWithPredicate(mapId, ray, ignoredEnt, wrapper, maxLength, returnOnFirstHit);
        }

        /// <summary>
        ///     Computes how much of the ray's path lies inside hard fixtures matching the ray's collision mask.
        /// </summary>
        /// <param name="mapId">The map to cast on.</param>
        /// <param name="ray">The ray to cast.</param>
        /// <param name="maxLength">Maximum ray length in meters.</param>
        /// <param name="ignoredEnt">An entity to ignore; null ignores nothing.</param>
        /// <returns>The summed distance travelled inside matching proxies.</returns>
        public float IntersectRayPenetration(
            MapId mapId,
            CollisionRay ray,
            float maxLength,
            EntityUid? ignoredEnt = null)
        {
            var penetration = 0f;
            var endPoint = ray.Position + ray.Direction.Normalized() * maxLength;
            var rayBox = new Box2(Vector2.Min(ray.Position, endPoint), Vector2.Max(ray.Position, endPoint));

            _broadphase.GetBroadphases(mapId, rayBox, (Entity<BroadphaseComponent> broadphase) =>
            {
                var (_, rot, invMatrix) = _transform.GetWorldPositionRotationInvMatrix(broadphase);

                var localPosition = Vector2.Transform(ray.Position, invMatrix);
                var gridRot = new Angle(-rot.Theta);
                var localDirection = gridRot.RotateVec(ray.Direction);
                var gridRay = new CollisionRay(localPosition, localDirection, ray.CollisionMask);

                bool Visit(in FixtureProxy proxy, in Vector2 point, float distFromOrigin)
                {
                    if (distFromOrigin > maxLength || proxy.Entity == ignoredEnt)
                        return true;

                    if (!proxy.Fixture.Hard)
                        return true;

                    if ((proxy.Fixture.CollisionLayer & ray.CollisionMask) == 0)
                        return true;

                    // Cast backwards from beyond the proxy to find where the ray leaves it.
                    var reverseOrigin = point + gridRay.Direction * proxy.AABB.Size.Length() * 2;
                    var reverseRay = new Ray(reverseOrigin, -gridRay.Direction);

                    if (reverseRay.Intersects(proxy.AABB, out _, out var exitPoint))
                        penetration += (point - exitPoint).Length();

                    return true;
                }

                broadphase.Comp.StaticTree.QueryRay(Visit, gridRay);
                broadphase.Comp.DynamicTree.QueryRay(Visit, gridRay);
            });

#if DEBUG
            _sharedDebugRaySystem.ReceiveLocalRayFromAnyThread(
                new(ray, maxLength, null, _netMan.IsServer, mapId));
#endif

            return penetration;
        }

        #endregion

        #region Distance

        /// <summary>
        /// Gets the nearest distance of 2 entities, ignoring any sensor proxies.
        /// </summary>
        public bool TryGetDistance(EntityUid uidA, EntityUid uidB,
            out float distance,
            TransformComponent? xformA = null, TransformComponent? xformB = null,
            FixturesComponent? managerA = null, FixturesComponent? managerB = null,
            PhysicsComponent? bodyA = null, PhysicsComponent? bodyB = null)
        {
            return TryGetNearest(uidA, uidB, out _, out _, out distance, xformA, xformB, managerA, managerB, bodyA, bodyB);
        }

        /// <summary>
        /// Get the nearest non-sensor points on entity A and entity B to each other.
        /// </summary>
        public bool TryGetNearestPoints(EntityUid uidA, EntityUid uidB,
            out Vector2 pointA, out Vector2 pointB,
            TransformComponent? xformA = null, TransformComponent? xformB = null,
            FixturesComponent? managerA = null, FixturesComponent? managerB = null,
            PhysicsComponent? bodyA = null, PhysicsComponent? bodyB = null)
        {
            return TryGetNearest(uidA, uidB, out pointA, out pointB, out _, xformA, xformB, managerA, managerB, bodyA, bodyB);
        }

        public bool TryGetNearest(EntityUid uidA, EntityUid uidB,
            out Vector2 pointA,
            out Vector2 pointB,
            out float distance,
            Transform xfA, Transform xfB,
            FixturesComponent? managerA = null, FixturesComponent? managerB = null,
            PhysicsComponent? bodyA = null, PhysicsComponent? bodyB = null)
        {
            pointA = Vector2.Zero;
            pointB = Vector2.Zero;

            if (!Resolve(uidA, ref managerA, ref bodyA) ||
                !Resolve(uidB, ref managerB,  ref bodyB) ||
                managerA.FixtureCount == 0 ||
                managerB.FixtureCount == 0)
            {
                distance = 0f;
                return false;
            }

            distance = float.MaxValue;
            var input = new DistanceInput
            {
                TransformA = xfA,
                TransformB = xfB,
                UseRadii = true
            };

            // No requirement on collision being enabled so chainshapes will fail
            foreach (var fixtureA in managerA.Fixtures.Values)
            {
                if (bodyA.Hard && !fixtureA.Hard)
                    continue;

                for (var i = 0; i < fixtureA.Shape.ChildCount; i++)
                {
                    input.ProxyA.Set(fixtureA.Shape, i);

                    foreach (var fixtureB in managerB.Fixtures.Values)
                    {
                        if (bodyB.Hard && !fixtureB.Hard)
                            continue;

                        for (var j = 0; j < fixtureB.Shape.ChildCount; j++)
                        {
                            input.ProxyB.Set(fixtureB.Shape, j);
                            DistanceManager.ComputeDistance(out var output, out _, input);

                            if (distance < output.Distance)
                                continue;

                            pointA = output.PointA;
                            pointB = output.PointB;
                            distance = output.Distance;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Gets the nearest points in map terms and the distance between them.
        /// If a body is hard it only considers hard fixtures.
        /// </summary>
        public bool TryGetNearest(EntityUid uid, MapCoordinates coordinates,
            out Vector2 point, out float distance,
            TransformComponent? xformA = null, FixturesComponent? manager = null, PhysicsComponent? body = null)
        {
            if (!Resolve(uid, ref xformA) ||
                xformA.MapID != coordinates.MapId)
            {
                point = Vector2.Zero;
                distance = 0f;
                return false;
            }

            point = Vector2.Zero;

            if (!Resolve(uid, ref manager, ref body) ||
                manager.FixtureCount == 0)
            {
                distance = 0f;
                return false;
            }

            var xfA = GetPhysicsTransform(uid, xformA);
            var xfB = new Transform(coordinates.Position, Angle.Zero);

            distance = float.MaxValue;
            var input = new DistanceInput();

            input.TransformA = xfA;
            input.TransformB = xfB;
            input.UseRadii = true;
            var pointShape = new PhysShapeCircle(10 * float.Epsilon, Vector2.Zero);

            // No requirement on collision being enabled so chainshapes will fail
            foreach (var fixtureA in manager.Fixtures.Values)
            {
                // We ignore non-hard fixtures if there is at least one hard fixture (i.e., if the body is hard)
                if (body.Hard && !fixtureA.Hard)
                    continue;

                DebugTools.Assert(fixtureA.ProxyCount <= 1);

                input.ProxyA.Set(fixtureA.Shape, 0);
                input.ProxyB.Set(pointShape, 0);
                DistanceManager.ComputeDistance(out var output, out _, input);

                if (distance < output.Distance)
                    continue;

                point = output.PointA;
                distance = output.Distance;
            }

            return true;
        }

        /// <summary>
        /// Gets the nearest points in map terms and the distance between them.
        /// If a body is hard it only considers hard fixtures.
        /// </summary>
        public bool TryGetNearest(EntityUid uidA, EntityUid uidB,
            out Vector2 point,
            out Vector2 pointB,
            out float distance,
            TransformComponent? xformA = null, TransformComponent? xformB = null,
            FixturesComponent? managerA = null, FixturesComponent? managerB = null,
            PhysicsComponent? bodyA = null, PhysicsComponent? bodyB = null)
        {
            if (!Resolve(uidA, ref xformA) || !Resolve(uidB, ref xformB) ||
                xformA.MapID != xformB.MapID)
            {
                point = Vector2.Zero;
                pointB = Vector2.Zero;
                distance = 0f;
                return false;
            }

            var xfA = GetPhysicsTransform(uidA, xformA);
            var xfB = GetPhysicsTransform(uidB, xformB);

            return TryGetNearest(uidA, uidB, out point, out pointB, out distance, xfA, xfB, managerA, managerB, bodyA, bodyB);
        }

        #endregion
    }
}
