using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Deform;

namespace Deform
{
    [Deformer(Name = "Touch Push", Type = typeof(TouchPushDeformer))]
    public class TouchPushDeformer : Deformer
    {
        public Transform Axis;
        public float Radius = 0.05f;
        public float Factor = 0f;

        [HideInInspector]
        public Vector3 PushDirection = Vector3.up; // クリック点の法線をここに渡す

        public override DataFlags DataFlags => DataFlags.Vertices;

        public override JobHandle Process(MeshData data, JobHandle dependency = default)
        {
            if (Axis == null) return dependency;

            var meshToAxis = Axis.worldToLocalMatrix * transform.localToWorldMatrix;

            // ワールド方向 → メッシュのローカル方向に変換
            float3 localPushDir = math.normalize(
                transform.InverseTransformDirection(PushDirection)
            );

            return new PushJob
            {
                meshToAxis = meshToAxis,
                radius = Radius,
                factor = Factor,
                pushDir = localPushDir,
                vertices = data.DynamicNative.VertexBuffer
            }.Schedule(data.Length, 64, dependency);
        }

        [BurstCompile]
        private struct PushJob : IJobParallelFor
        {
            public float4x4 meshToAxis;
            public float radius;
            public float factor;
            public float3 pushDir;
            public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                float3 posInAxisSpace = math.transform(meshToAxis, vertices[index]);
                float dist = math.length(posInAxisSpace);

                if (dist < radius)
                {
                    float t = 1f - (dist / radius);
                    t = t * t * (3f - 2f * t);
                    vertices[index] += pushDir * factor * t; // 全頂点が同じ方向に動く
                }
            }
        }
    }
}