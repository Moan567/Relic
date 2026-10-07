using Microsoft.Xna.Framework;
namespace Engine.Physics.Constraints;

public interface Constraint
{
    public bool IsAttached { get; set; }
    public void Attach(WorldEntity entityA, WorldEntity entityB);
    public void Detach();
}

public class FixedConstraint : Constraint
{
    public bool IsAttached { get; set; }
    public bool DisableAutoPointDetection;
    WorldEntity entityA;
    WorldEntity entityB;

    JoltPhysicsSharp.FixedConstraint fixedConstraint;

    public void Attach(WorldEntity entityA, WorldEntity entityB)
    {
        Detach();

        this.entityA = entityA;
        this.entityB = entityB;
            
        var settings = new JoltPhysicsSharp.FixedConstraintSettings
        {
            AutoDetectPoint = !DisableAutoPointDetection,
        };
        if (DisableAutoPointDetection)
        {
            settings.Point1 = (entityA.Position+entityA.BodyOffset).ToNumerics();
            settings.Point2 = (entityB.Position+entityB.BodyOffset).ToNumerics();
        }
        fixedConstraint = new (settings, entityA.PhysicsBody, entityB.PhysicsBody);

        PhysicsEngine.BodyInterface.ActivateBody(entityA.PhysicsBodyID);
        
        PhysicsEngine.ConstraintsToAdd.Enqueue(fixedConstraint);

        IsAttached = true;
    }

    public void Detach()
    {
        if (fixedConstraint != null)
        {
            PhysicsEngine.PhysicsSystem.RemoveConstraint(fixedConstraint);
            if (entityA != null) entityA.WasJustReleasedFromConstraint = true;
            if(entityB != null) entityB.WasJustReleasedFromConstraint = true;
            fixedConstraint = null;
        }
        IsAttached = false;
    }
}
public class PointConstraint : Constraint
{
    public bool IsAttached { get; set; }
    WorldEntity entityA;
    WorldEntity entityB;

    JoltPhysicsSharp.PointConstraint pointConstraint;

    public void Attach(WorldEntity entityA, WorldEntity entityB)
    {
        Detach();

        this.entityA = entityA;
        this.entityB = entityB;

        var settings = new JoltPhysicsSharp.PointConstraintSettings
        {
            Point1 = entityA.Position.ToNumerics(),
            Point2 = entityB.Position.ToNumerics(),
        };
        pointConstraint = new(settings, entityA.PhysicsBody, entityB.PhysicsBody);

        PhysicsEngine.BodyInterface.ActivateBody(entityA.PhysicsBodyID);

        PhysicsEngine.ConstraintsToAdd.Enqueue(pointConstraint);

        IsAttached = true;
    }

    public void Detach()
    {
        if (pointConstraint != null)
        {
            PhysicsEngine.PhysicsSystem.RemoveConstraint(pointConstraint);
            pointConstraint = null;
        }
        IsAttached = false;
    }
}
public class SpringConstraint : Constraint
{
    public bool IsAttached { get; set; }

    public bool AutoMaxDistance = true;
    public float MinDistance = 0f;
    public float MaxDistance = 0f;
    public float Damping = 0.5f;
    public float Frequency = 2f;

    WorldEntity entityA;
    WorldEntity entityB;

    JoltPhysicsSharp.DistanceConstraint distanceConstraint;

    public void Attach(WorldEntity entityA, WorldEntity entityB)
    {
        Detach();

        this.entityA = entityA;
        this.entityB = entityB;

        var settings = new JoltPhysicsSharp.DistanceConstraintSettings
        {
            Point1 = entityA.Position.ToNumerics(),
            Point2 = entityB.Position.ToNumerics(),
            MaxDistance = AutoMaxDistance ? Vector3.Distance(entityA.OrientedBounds.Center, entityB.OrientedBounds.Center) : MaxDistance,
            MinDistance = MinDistance,
            LimitsSpringSettings = new JoltPhysicsSharp.SpringSettings
            {
                Damping = Damping,
                Mode = JoltPhysicsSharp.SpringMode.FrequencyAndDamping,
                FrequencyOrStiffness = Frequency
            }
        };
        distanceConstraint = new(settings, entityA.PhysicsBody, entityB.PhysicsBody);

        PhysicsEngine.BodyInterface.ActivateBody(entityA.PhysicsBodyID);

        PhysicsEngine.ConstraintsToAdd.Enqueue(distanceConstraint);

        IsAttached = true;
    }

    public void Detach()
    {
        if (distanceConstraint != null)
        {
            entityA.Velocity = entityA.GetPreviousVelocity();

            PhysicsEngine.PhysicsSystem.RemoveConstraint(distanceConstraint);
            distanceConstraint = null;
        }
        IsAttached = false;
    }
}