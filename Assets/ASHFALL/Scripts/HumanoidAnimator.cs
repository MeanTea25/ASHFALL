using System.Collections;
using UnityEngine;

/// <summary>
/// Procedural animation for the part-based characters (no rig / Animator Controller needed).
///   Locomotion: walk cycle driven by how fast the parent object is moving, idle sway when still.
///   PlayStab(): the player's backstab (raise knife, thrust, hold, pull back).
///   DeathRoutine(): the enemy's death (stabbed -> reacts -> collapses -> falls -> lies down).
/// Limb pivots are at the shoulders / hips. Negative X rotation swings a limb FORWARD.
/// </summary>
public class HumanoidAnimator : MonoBehaviour
{
    public Transform legL, legR, armL, armR;
    public float legSwing = 40f;
    public float armSwing = 30f;
    public float smoothing = 18f;

    public bool IsPlaying { get; private set; }

    struct Pose
    {
        public float armL, armR, legL, legR, spread, pitch, drop;
        public Pose(float armL, float armR, float legL, float legR, float spread, float pitch, float drop)
        {
            this.armL = armL; this.armR = armR; this.legL = legL; this.legR = legR;
            this.spread = spread; this.pitch = pitch; this.drop = drop;
        }
    }

    static readonly Pose Rest = new Pose(0, 0, 0, 0, 0, 0, 0);

    Transform owner;
    Vector3 lastPos, restPos;
    Pose current = Rest;
    float speed, phase;

    void Awake()
    {
        owner = transform.parent != null ? transform.parent : transform;
        lastPos = owner.position;
        restPos = transform.localPosition;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 d = owner.position - lastPos;
        d.y = 0f;
        lastPos = owner.position;
        float inst = Mathf.Min(d.magnitude / dt, 12f);
        speed = Mathf.Lerp(speed, inst, 1f - Mathf.Exp(-12f * dt));

        if (IsPlaying) return;

        phase += (4f + speed * 1.6f) * dt;
        float move = Mathf.Clamp01(speed / 1.5f);
        float s = Mathf.Sin(phase);
        float idle = Mathf.Sin(Time.time * 1.6f) * 2.5f * (1f - move);

        float k = 1f - Mathf.Exp(-smoothing * dt);
        Blend(legL, Quaternion.Euler(s * legSwing * move, 0f, 0f), k);
        Blend(legR, Quaternion.Euler(-s * legSwing * move, 0f, 0f), k);
        Blend(armL, Quaternion.Euler(-s * armSwing * move + idle, 0f, 0f), k);
        Blend(armR, Quaternion.Euler(s * armSwing * move - idle, 0f, 0f), k);
    }

    static void Blend(Transform t, Quaternion target, float k)
    {
        if (t != null) t.localRotation = Quaternion.Slerp(t.localRotation, target, k);
    }

    // ------------------------------------------------------------- pose engine
    void Apply(Pose p)
    {
        current = p;
        if (armL != null) armL.localRotation = Quaternion.Euler(p.armL, 0f, -p.spread);
        if (armR != null) armR.localRotation = Quaternion.Euler(p.armR, 0f, p.spread);
        if (legL != null) legL.localRotation = Quaternion.Euler(p.legL, 0f, 0f);
        if (legR != null) legR.localRotation = Quaternion.Euler(p.legR, 0f, 0f);
        transform.localRotation = Quaternion.Euler(p.pitch, 0f, 0f);
        transform.localPosition = restPos + Vector3.up * p.drop;
    }

    static Pose Mix(Pose a, Pose b, float t)
    {
        return new Pose(
            Mathf.Lerp(a.armL, b.armL, t), Mathf.Lerp(a.armR, b.armR, t),
            Mathf.Lerp(a.legL, b.legL, t), Mathf.Lerp(a.legR, b.legR, t),
            Mathf.Lerp(a.spread, b.spread, t), Mathf.Lerp(a.pitch, b.pitch, t), Mathf.Lerp(a.drop, b.drop, t));
    }

    IEnumerator Go(Pose target, float duration)
    {
        Pose from = current;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(duration, 0.01f);
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            Apply(Mix(from, target, e));
            yield return null;
        }
        Apply(target);
    }

    // -------------------------------------------------------------- the stab
    public void PlayStab()
    {
        if (IsPlaying) return;
        StartCoroutine(StabRoutine());
    }

    IEnumerator StabRoutine()
    {
        IsPlaying = true;
        yield return Go(new Pose(10f, -55f, -10f, 10f, 0f, 0f, 0f), 0.12f);    // 1. raise the knife
        yield return Go(new Pose(25f, -95f, -35f, 25f, 0f, 12f, 0f), 0.07f);   // 2. thrust forward, lean in
        yield return Go(new Pose(25f, -95f, -35f, 25f, 0f, 12f, 0f), 0.12f);   // 3. hold
        yield return Go(new Pose(15f, -40f, -10f, 10f, 0f, 4f, 0f), 0.15f);    // 4. pull back
        yield return Go(Rest, 0.15f);                                          // 5. back to ready
        IsPlaying = false;
    }

    // ------------------------------------------------------------- the death
    public IEnumerator DeathRoutine()
    {
        IsPlaying = true;
        yield return Go(new Pose(-20f, -20f, 0f, 0f, 35f, -12f, 0f), 0.15f);       // 1. stabbed: arches back, arms fling out
        yield return Go(new Pose(-30f, -30f, -25f, 20f, 70f, 15f, 0f), 0.35f);     // 2. reacts: staggers
        yield return Go(new Pose(-70f, -70f, 55f, 55f, 10f, 45f, -0.35f), 0.45f);  // 3. collapses onto knees
        yield return Go(new Pose(-80f, -80f, 25f, 25f, 20f, 75f, -0.25f), 0.4f);   // 4. falls forward
        yield return Go(new Pose(0f, 0f, 0f, 0f, 15f, 90f, 0.14f), 0.4f);          // 5. lies still
    }

    public void PlayDeath() { if (!IsPlaying) StartCoroutine(DeathRoutine()); }
    public void PlayJump() { if (!IsPlaying) StartCoroutine(JumpRoutine()); }
    public void PlayLand() { if (!IsPlaying) StartCoroutine(LandRoutine()); }
    IEnumerator JumpRoutine()
    {
        IsPlaying = true;
        yield return Go(new Pose(-32f, 32f, 25f, -25f, 10f, -8f, .12f), .12f);
        yield return new WaitForSeconds(.23f);
        yield return Go(Rest, .16f);
        IsPlaying = false;
    }
    IEnumerator LandRoutine()
    {
        IsPlaying = true;
        yield return Go(new Pose(12f, 12f, -28f, -28f, 0f, 15f, -.10f), .09f);
        yield return Go(Rest, .17f);
        IsPlaying = false;
    }
    public void ResetPose()
    {
        StopAllCoroutines();
        IsPlaying = false;
        Apply(Rest);
        lastPos = owner.position;
    }
}
