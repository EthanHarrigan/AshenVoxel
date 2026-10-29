using System;
using System.Collections.Generic;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

namespace AshenVoxel.Exploration
{
    public sealed partial class VegetationStreaming
    {
        /// <summary>Optional render surface. Population, roots and collision retain
        /// their original source anchors; no terrain publication regenerates trees.</summary>
        public WorldExplorer TerrainView { get; set; }
        public const double ClipmapExactRadius = 400;
        public long ClipmapAnchorVisits { get; private set; }
        List<Batch> anchorBatches;
        TerrainClipmap anchorRenderer;
        long anchorRevision = -1;
        WorldPoint anchorObserver;
        int anchorBatch, anchorInstance;
        bool anchoring, anchorHasBounds;
        Bounds anchorBounds;

        double RenderedAnchorHeight(WorldPoint source)
        {
            var clipmap = TerrainView != null && TerrainView.UsingClipmap ? TerrainView.ClipmapRenderer : null;
            double dx = source.X - observer.X, dz = source.Z - observer.Z;
            if (clipmap != null && dx * dx + dz * dz > ClipmapExactRadius * ClipmapExactRadius &&
                clipmap.TryRenderedHeight(source.X, source.Z, out double height)) return height - .05;
            return source.Y;
        }

        void TickClipmapAnchors(long frameStart)
        {
            var renderer = TerrainView != null && TerrainView.UsingClipmap ? TerrainView.ClipmapRenderer : null;
            long revision = renderer != null ? renderer.DataRevision : 0;
            double dx = observer.X - anchorObserver.X, dz = observer.Z - anchorObserver.Z;
            bool changedBatch = !ReferenceEquals(anchorBatches, batches);
            if (changedBatch || (!anchoring && (renderer != anchorRenderer || revision != anchorRevision || dx * dx + dz * dz > 16)))
            {
                anchorBatches = batches; anchorRenderer = renderer; anchorRevision = revision; anchorObserver = observer;
                anchorBatch = anchorInstance = 0; anchorHasBounds = false; anchoring = true;
            }
            // Finish the current pass before chasing later revisions. Continuous
            // terrain movement must not repeatedly restart and starve far entries.
            int visited = 0; long started = Now; double budget = Math.Min(.4, Math.Max(.1, WorkMilliseconds * .2));
            while (anchoring && anchorBatch < batches.Count && visited < 256 && Ms(Now - started) < budget && Ms(Now - frameStart) < Math.Max(.25, WorkMilliseconds))
            {
                Batch batch = batches[anchorBatch];
                if (anchorInstance >= batch.Count)
                {
                    if (anchorHasBounds) batch.Bounds = anchorBounds;
                    anchorBatch++; anchorInstance = 0; anchorHasBounds = false; continue;
                }
                int index = anchorInstance++; visited++; ClipmapAnchorVisits++;
                WorldPoint source = batch.Anchors[index];
                Matrix4x4 matrix = batch.Matrices[index];
                matrix.m13 = (float)(RenderedAnchorHeight(source) - origin.Y);
                batch.Matrices[index] = matrix;
                Bounds mesh = batch.Mesh.bounds;
                float radius = Math.Max(Math.Abs(mesh.min.x), Math.Max(Math.Abs(mesh.max.x),
                    Math.Max(Math.Abs(mesh.min.z), Math.Abs(mesh.max.z)))) * 1.415f + .1f;
                var bounds = new Bounds(new Vector3(matrix.m03, matrix.m13 + mesh.center.y, matrix.m23),
                    new Vector3(radius * 2, mesh.size.y + .2f, radius * 2));
                // Preserve conservative bounds while a partial batch is being
                // updated, then replace them with the tight accumulated bounds.
                batch.Bounds.Encapsulate(bounds);
                if (anchorHasBounds) anchorBounds.Encapsulate(bounds); else { anchorBounds = bounds; anchorHasBounds = true; }
            }
            if (anchorBatch >= batches.Count) anchoring = false;
        }

        void ResetClipmapAnchors()
        {
            anchorBatches = null; anchorRenderer = null; anchorRevision = -1;
            anchoring = anchorHasBounds = false; anchorBatch = anchorInstance = 0; ClipmapAnchorVisits = 0;
        }

        public readonly struct RenderedVegetationAnchor
        {
            public readonly int PrototypeId, Lod;
            public readonly WorldPoint SourcePosition, RenderPosition;
            public RenderedVegetationAnchor(int prototypeId, int lod, WorldPoint source, WorldPoint render)
            { PrototypeId = prototypeId; Lod = lod; SourcePosition = source; RenderPosition = render; }
        }
        /// <summary>Validation-only enumeration of actual published draw anchors.
        /// Split root parts are not counted as additional trees.</summary>
        public IEnumerable<RenderedVegetationAnchor> EnumerateRenderedAnchors()
        {
            foreach (Batch batch in batches) if (batch.CountsInstance)
                for (int i = 0; i < batch.Count; i++)
                {
                    WorldPoint source = batch.Anchors[i]; Matrix4x4 matrix = batch.Matrices[i];
                    yield return new RenderedVegetationAnchor(batch.ModelId, batch.Lod, source,
                        new WorldPoint(source.X, matrix.m13 + origin.Y, source.Z));
                }
        }
    }
}
