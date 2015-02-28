/******************************************************************************
 * Spine Runtimes Software License
 * Version 2.1
 * 
 * Copyright (c) 2013, Esoteric Software
 * All rights reserved.
 * 
 * You are granted a perpetual, non-exclusive, non-sublicensable and
 * non-transferable license to install, execute and perform the Spine Runtimes
 * Software (the "Software") solely for internal use. Without the written
 * permission of Esoteric Software (typically granted by licensing Spine), you
 * may not (a) modify, translate, adapt or otherwise create derivative works,
 * improvements of the Software or develop new applications using the Software
 * or (b) remove, delete, alter or obscure any trademarks or any copyright,
 * trademark, patent or other intellectual property or proprietary rights
 * notices on or in the Software, including any copy thereof. Redistributions
 * in binary or source form must include this license and terms.
 * 
 * THIS SOFTWARE IS PROVIDED BY ESOTERIC SOFTWARE "AS IS" AND ANY EXPRESS OR
 * IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF
 * MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO
 * EVENT SHALL ESOTERIC SOFTARE BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS;
 * OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR
 * OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
 * ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *****************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;
using Spine;

/// <summary>Renders a skeleton.</summary>
[ExecuteInEditMode, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SkeletonRenderer : MonoBehaviour {

	public delegate void SkeletonRendererDelegate (SkeletonRenderer skeletonRenderer);

	public SkeletonRendererDelegate OnReset;
	[System.NonSerialized]
	public bool valid;
	[System.NonSerialized]
	public Skeleton skeleton;
	public SkeletonDataAsset skeletonDataAsset;
	public String initialSkinName;
	public bool calculateNormals, calculateTangents;
	public float zSpacing;
	public bool renderMeshes = true, immutableTriangles;
	public bool frontFacing;
	public bool logErrors = false;

	[SpineSlot]
	public string[] submeshSeparators = new string[0];

	[HideInInspector]
	public List<Slot> submeshSeparatorSlots = new List<Slot>();


	private Renderer meshRenderer;
	private MeshFilter meshFilter;
	private Mesh mesh1, mesh2;
	private bool useMesh1;
	private float[] tempVertices = new float[8];
	private Vector3[] vertices;
	private Color32[] colors;
	private Vector2[] uvs;
	private Material[] sharedMaterials = new Material[0];
	private LastState lastState = new LastState();
	private readonly ExposedList<Material> submeshMaterials = new ExposedList<Material>();
	private readonly ExposedList<Submesh> submeshes = new ExposedList<Submesh>();

	public virtual void Reset () {
		if (meshFilter != null)
			meshFilter.sharedMesh = null;

		if (meshRenderer != null)
			meshRenderer.sharedMaterial = null;

		if (mesh1 != null) {
			if (Application.isPlaying)
				Destroy(mesh1);
			else
				DestroyImmediate(mesh1);
		}

		if (mesh2 != null) {
			if (Application.isPlaying)
				Destroy(mesh2);
			else
				DestroyImmediate(mesh2);
		}

		lastState = new LastState();
		mesh1 = null;
		mesh2 = null;
		vertices = null;
		colors = null;
		uvs = null;
		sharedMaterials = new Material[0];
		submeshMaterials.Clear();
		submeshes.Clear();
		skeleton = null;

		valid = false;
		if (!skeletonDataAsset) {
			if (logErrors)
				Debug.LogError("Missing SkeletonData asset.", this);

			return;
		}
		SkeletonData skeletonData = skeletonDataAsset.GetSkeletonData(false);
		if (skeletonData == null)
			return;
		valid = true;

		meshFilter = GetComponent<MeshFilter>();
		meshRenderer = GetComponent<Renderer>();
		mesh1 = newMesh();
		mesh2 = newMesh();
		vertices = new Vector3[0];

		skeleton = new Skeleton(skeletonData);
		if (initialSkinName != null && initialSkinName.Length > 0 && initialSkinName != "default")
			skeleton.SetSkin(initialSkinName);

		submeshSeparatorSlots.Clear();
		for (int i = 0; i < submeshSeparators.Length; i++) {
			submeshSeparatorSlots.Add(skeleton.FindSlot(submeshSeparators[i]));
		}

		if (OnReset != null)
			OnReset(this);
	}

	public virtual void Awake () {
		Reset();
	}

	public virtual void OnDestroy () {
		if (mesh1 != null) {
			if (Application.isPlaying)
				Destroy(mesh1);
			else
				DestroyImmediate(mesh1);
		}

		if (mesh2 != null) {
			if (Application.isPlaying)
				Destroy(mesh2);
			else
				DestroyImmediate(mesh2);
		}

		mesh1 = null;
		mesh2 = null;
	}

	private Mesh newMesh () {
		Mesh mesh = new Mesh();
		mesh.name = "Skeleton Mesh";
		mesh.hideFlags = HideFlags.HideAndDontSave;
		mesh.MarkDynamic();
		return mesh;
	}

	public virtual void LateUpdate () {
		if (!valid || !meshRenderer.enabled)
			return;

		// Count vertices and submesh triangles.
		int vertexCount = 0;
		int submeshTriangleCount = 0, submeshFirstVertex = 0, submeshStartSlotIndex = 0;
		Material lastMaterial = null;
		ExposedList<Slot> drawOrder = skeleton.drawOrder;
		int drawOrderCount = drawOrder.Count;
		int submeshSeparatorSlotsCount = submeshSeparatorSlots.Count;
		bool renderMeshes = this.renderMeshes;

		// Clear last state of attachments and submeshes
		ExposedList<int> attachmentsTriangleCountTemp = lastState.attachmentsTriangleCountTemp;
		attachmentsTriangleCountTemp.GrowIfNeeded(drawOrderCount);
		attachmentsTriangleCountTemp.Count = drawOrderCount;

		ExposedList<LastState.AddSubmeshArguments> addSubmeshArgumentsTemp = lastState.addSubmeshArgumentsTemp;
		addSubmeshArgumentsTemp.Clear(false);
		for (int i = 0; i < drawOrderCount; i++) {
			Slot slot = drawOrder.Items[i];
			Attachment attachment = slot.attachment;

			object rendererObject;
			int attachmentVertexCount, attachmentTriangleCount;

			attachmentsTriangleCountTemp.Items[i] = -1;
			RegionAttachment regionAttachment = attachment as RegionAttachment;
			if (regionAttachment != null) {
				rendererObject = regionAttachment.RendererObject;
				attachmentVertexCount = 4;
				attachmentTriangleCount = 6;
			} else {
				if (!renderMeshes)
					continue;
				MeshAttachment meshAttachment = attachment as MeshAttachment;
				if (meshAttachment != null) {
					rendererObject = meshAttachment.RendererObject;
					attachmentVertexCount = meshAttachment.vertices.Length >> 1;
					attachmentTriangleCount = meshAttachment.triangles.Length;
				} else {
					SkinnedMeshAttachment skinnedMeshAttachment = attachment as SkinnedMeshAttachment;
					if (skinnedMeshAttachment != null) {
						rendererObject = skinnedMeshAttachment.RendererObject;
						attachmentVertexCount = skinnedMeshAttachment.uvs.Length >> 1;
						attachmentTriangleCount = skinnedMeshAttachment.triangles.Length;
					} else
						continue;
				}
			}

			// Populate submesh when material changes.
			Material material = (Material)((AtlasRegion)rendererObject).page.rendererObject;
			if ((lastMaterial != null && lastMaterial.GetInstanceID() != material.GetInstanceID()) || 
				(submeshSeparatorSlotsCount > 0 && submeshSeparatorSlots.Contains(slot))) {
				addSubmeshArgumentsTemp.Add(
					new LastState.AddSubmeshArguments(lastMaterial, submeshStartSlotIndex, i, submeshTriangleCount, submeshFirstVertex, false)
					);
				submeshTriangleCount = 0;
				submeshFirstVertex = vertexCount;
				submeshStartSlotIndex = i;
			}
			lastMaterial = material;

			submeshTriangleCount += attachmentTriangleCount;
			vertexCount += attachmentVertexCount;

			attachmentsTriangleCountTemp.Items[i] = attachmentTriangleCount;
		}
		addSubmeshArgumentsTemp.Add(
			new LastState.AddSubmeshArguments(lastMaterial, submeshStartSlotIndex, drawOrderCount, submeshTriangleCount, submeshFirstVertex, true)
			);
		
		bool mustUpdateMeshStructure = MustUpdateMeshStructure(attachmentsTriangleCountTemp, addSubmeshArgumentsTemp);
		if (mustUpdateMeshStructure) {
			submeshMaterials.Clear();
			for (int i = 0, n = addSubmeshArgumentsTemp.Count; i < n; i++) {
				LastState.AddSubmeshArguments arguments = addSubmeshArgumentsTemp.Items[i];
				AddSubmesh(
					arguments.material,
					arguments.startSlot,
					arguments.endSlot,
					arguments.triangleCount,
					arguments.firstVertex,
					arguments.lastSubmesh
				);
			}

			// Set materials.
			if (submeshMaterials.Count == sharedMaterials.Length)
				submeshMaterials.CopyTo(sharedMaterials);
			else
				sharedMaterials = submeshMaterials.ToArray();

			meshRenderer.sharedMaterials = sharedMaterials;
		}

		// Ensure mesh data is the right size.
		Vector3[] vertices = this.vertices;
		bool newTriangles = vertexCount > vertices.Length;
		if (newTriangles) {
			// Not enough vertices, increase size.
			this.vertices = vertices = new Vector3[vertexCount];
			this.colors = new Color32[vertexCount];
			this.uvs = new Vector2[vertexCount];
			mesh1.Clear();
			mesh2.Clear();
		} else {
			// Too many vertices, zero the extra.
			Vector3 zero = Vector3.zero;
			for (int i = vertexCount, n = lastState.vertexCount ; i < n; i++)
				vertices[i] = zero;
		}
		lastState.vertexCount = vertexCount;

		// Setup mesh.
		Vector3 meshBoundsMin;
		meshBoundsMin.x = float.MaxValue;
		meshBoundsMin.y = float.MaxValue;
		meshBoundsMin.z = float.MaxValue;
		Vector3 meshBoundsMax;
		meshBoundsMax.x = float.MinValue;
		meshBoundsMax.y = float.MinValue;
		meshBoundsMax.z = float.MinValue;
		float[] tempVertices = this.tempVertices;
		Vector2[] uvs = this.uvs;
		Color32[] colors = this.colors;
		int vertexIndex = 0;
		Color32 color = new Color32();
		float zSpacing = this.zSpacing;
		float a = skeleton.a * 255, r = skeleton.r, g = skeleton.g, b = skeleton.b;
		for (int i = 0; i < drawOrderCount; i++) {
			Slot slot = drawOrder.Items[i];
			Attachment attachment = slot.attachment;
			RegionAttachment regionAttachment = attachment as RegionAttachment;
			if (regionAttachment != null) {
				regionAttachment.ComputeWorldVertices(slot.bone, tempVertices);

				float z = i * zSpacing;
				vertices[vertexIndex].x = tempVertices[RegionAttachment.X1];
				vertices[vertexIndex].y = tempVertices[RegionAttachment.Y1];
				vertices[vertexIndex].z = z;
				vertices[vertexIndex + 1].x = tempVertices[RegionAttachment.X4];
				vertices[vertexIndex + 1].y = tempVertices[RegionAttachment.Y4];
				vertices[vertexIndex + 1].z = z;
				vertices[vertexIndex + 2].x = tempVertices[RegionAttachment.X2];
				vertices[vertexIndex + 2].y = tempVertices[RegionAttachment.Y2];
				vertices[vertexIndex + 2].z = z;
				vertices[vertexIndex + 3].x = tempVertices[RegionAttachment.X3];
				vertices[vertexIndex + 3].y = tempVertices[RegionAttachment.Y3];
				vertices[vertexIndex + 3].z = z;

				color.a = slot.data.additiveBlending ? (byte) 0 : (byte)(a * slot.a * regionAttachment.a);
				color.r = (byte)(r * slot.r * regionAttachment.r * color.a);
				color.g = (byte)(g * slot.g * regionAttachment.g * color.a);
				color.b = (byte)(b * slot.b * regionAttachment.b * color.a);

				colors[vertexIndex] = color;
				colors[vertexIndex + 1] = color;
				colors[vertexIndex + 2] = color;
				colors[vertexIndex + 3] = color;

				float[] regionUVs = regionAttachment.uvs;
				uvs[vertexIndex].x = regionUVs[RegionAttachment.X1];
				uvs[vertexIndex].y = regionUVs[RegionAttachment.Y1];
				uvs[vertexIndex + 1].x = regionUVs[RegionAttachment.X4];
				uvs[vertexIndex + 1].y = regionUVs[RegionAttachment.Y4];
				uvs[vertexIndex + 2].x = regionUVs[RegionAttachment.X2];
				uvs[vertexIndex + 2].y = regionUVs[RegionAttachment.Y2];
				uvs[vertexIndex + 3].x = regionUVs[RegionAttachment.X3];
				uvs[vertexIndex + 3].y = regionUVs[RegionAttachment.Y3];

				// Calculate min/max X
				if (tempVertices[RegionAttachment.X1] < meshBoundsMin.x)
					meshBoundsMin.x = tempVertices[RegionAttachment.X1];
				else if (tempVertices[RegionAttachment.X1] > meshBoundsMax.x)
					meshBoundsMax.x = tempVertices[RegionAttachment.X1];
				if (tempVertices[RegionAttachment.X2] < meshBoundsMin.x)
					meshBoundsMin.x = tempVertices[RegionAttachment.X2];
				else if (tempVertices[RegionAttachment.X2] > meshBoundsMax.x)
					meshBoundsMax.x = tempVertices[RegionAttachment.X2];
				if (tempVertices[RegionAttachment.X3] < meshBoundsMin.x)
					meshBoundsMin.x = tempVertices[RegionAttachment.X3];
				else if (tempVertices[RegionAttachment.X3] > meshBoundsMax.x)
					meshBoundsMax.x = tempVertices[RegionAttachment.X3];
				if (tempVertices[RegionAttachment.X4] < meshBoundsMin.x)
					meshBoundsMin.x = tempVertices[RegionAttachment.X4];
				else if (tempVertices[RegionAttachment.X4] > meshBoundsMax.x)
					meshBoundsMax.x = tempVertices[RegionAttachment.X4];

				// Calculate min/max Y
				if (tempVertices[RegionAttachment.Y1] < meshBoundsMin.y)
					meshBoundsMin.y = tempVertices[RegionAttachment.Y1];
				else if (tempVertices[RegionAttachment.Y1] > meshBoundsMax.y)
					meshBoundsMax.y = tempVertices[RegionAttachment.Y1];
				if (tempVertices[RegionAttachment.Y2] < meshBoundsMin.y)
					meshBoundsMin.y = tempVertices[RegionAttachment.Y2];
				else if (tempVertices[RegionAttachment.Y2] > meshBoundsMax.y)
					meshBoundsMax.y = tempVertices[RegionAttachment.Y2];
				if (tempVertices[RegionAttachment.Y3] < meshBoundsMin.y)
					meshBoundsMin.y = tempVertices[RegionAttachment.Y3];
				else if (tempVertices[RegionAttachment.Y3] > meshBoundsMax.y)
					meshBoundsMax.y = tempVertices[RegionAttachment.Y3];
				if (tempVertices[RegionAttachment.Y4] < meshBoundsMin.y)
					meshBoundsMin.y = tempVertices[RegionAttachment.Y4];
				else if (tempVertices[RegionAttachment.Y4] > meshBoundsMax.y)
					meshBoundsMax.y = tempVertices[RegionAttachment.Y4];

				// Calculate min/max Z
				if (z < meshBoundsMin.z)
					meshBoundsMin.z = z;
				else if (z > meshBoundsMax.z)
					meshBoundsMax.z = z;

				vertexIndex += 4;
			} else {
				if (!renderMeshes)
					continue;
				MeshAttachment meshAttachment = attachment as MeshAttachment;
				if (meshAttachment != null) {
					int meshVertexCount = meshAttachment.vertices.Length;
					if (tempVertices.Length < meshVertexCount)
						this.tempVertices = tempVertices = new float[meshVertexCount];
					meshAttachment.ComputeWorldVertices(slot, tempVertices);

					color.a = slot.data.additiveBlending ? (byte) 0 : (byte)(a * slot.a * meshAttachment.a);
					color.r = (byte)(r * slot.r * meshAttachment.r * color.a);
					color.g = (byte)(g * slot.g * meshAttachment.g * color.a);
					color.b = (byte)(b * slot.b * meshAttachment.b * color.a);

					float[] meshUVs = meshAttachment.uvs;
					float z = i * zSpacing;
					for (int ii = 0; ii < meshVertexCount; ii += 2, vertexIndex++) {
						vertices[vertexIndex].x = tempVertices[ii];
						vertices[vertexIndex].y = tempVertices[ii + 1];
						vertices[vertexIndex].z = z;
						colors[vertexIndex] = color;
						uvs[vertexIndex].x = meshUVs[ii];
						uvs[vertexIndex].y = meshUVs[ii + 1];

						if (tempVertices[ii] < meshBoundsMin.x)
							meshBoundsMin.x = tempVertices[ii];
						else if (tempVertices[ii] > meshBoundsMax.x)
							meshBoundsMax.x = tempVertices[ii];
						if (tempVertices[ii + 1]< meshBoundsMin.y)
							meshBoundsMin.y = tempVertices[ii + 1];
						else if (tempVertices[ii + 1] > meshBoundsMax.y)
							meshBoundsMax.y = tempVertices[ii + 1];
						if (z < meshBoundsMin.z)
							meshBoundsMin.z = z;
						else if (z > meshBoundsMax.z)
							meshBoundsMax.z = z;
					}
				} else {
					SkinnedMeshAttachment skinnedMeshAttachment = attachment as SkinnedMeshAttachment;
					if (skinnedMeshAttachment != null) {
						int meshVertexCount = skinnedMeshAttachment.uvs.Length;
						if (tempVertices.Length < meshVertexCount)
							this.tempVertices = tempVertices = new float[meshVertexCount];
						skinnedMeshAttachment.ComputeWorldVertices(slot, tempVertices);

						color.a = slot.data.additiveBlending ? (byte) 0 : (byte)(a * slot.a * skinnedMeshAttachment.a);
						color.r = (byte)(r * slot.r * skinnedMeshAttachment.r * color.a);
						color.g = (byte)(g * slot.g * skinnedMeshAttachment.g * color.a);
						color.b = (byte)(b * slot.b * skinnedMeshAttachment.b * color.a);

						float[] meshUVs = skinnedMeshAttachment.uvs;
						float z = i * zSpacing;
						for (int ii = 0; ii < meshVertexCount; ii += 2, vertexIndex++) {
							vertices[vertexIndex].x = tempVertices[ii];
							vertices[vertexIndex].y = tempVertices[ii + 1];
							vertices[vertexIndex].z = z;
							colors[vertexIndex] = color;
							uvs[vertexIndex].x = meshUVs[ii];
							uvs[vertexIndex].y = meshUVs[ii + 1];

							if (tempVertices[ii] < meshBoundsMin.x)
								meshBoundsMin.x = tempVertices[ii];
							else if (tempVertices[ii] > meshBoundsMax.x)
								meshBoundsMax.x = tempVertices[ii];
							if (tempVertices[ii + 1]< meshBoundsMin.y)
								meshBoundsMin.y = tempVertices[ii + 1];
							else if (tempVertices[ii + 1] > meshBoundsMax.y)
								meshBoundsMax.y = tempVertices[ii + 1];
							if (z < meshBoundsMin.z)
								meshBoundsMin.z = z;
							else if (z > meshBoundsMax.z)
								meshBoundsMax.z = z;
						}
					}
				}
			}
		}

		// Double buffer mesh.
		Mesh mesh = useMesh1 ? mesh1 : mesh2;
		meshFilter.sharedMesh = mesh;

		mesh.vertices = vertices;
		mesh.colors32 = colors;
		mesh.uv = uvs;

		if (mustUpdateMeshStructure) {
			int submeshCount = submeshMaterials.Count;
			mesh.subMeshCount = submeshCount;
			for (int i = 0; i < submeshCount; ++i)
				mesh.SetTriangles(submeshes.Items[i].triangles, i);
		}

		Vector3 meshBoundsExtents = meshBoundsMax - meshBoundsMin;
		Vector3 meshBoundsCenter = meshBoundsMin + meshBoundsExtents * 0.5f;
		mesh.bounds = new Bounds(meshBoundsCenter, meshBoundsExtents);

		if (newTriangles && calculateNormals) {
			Vector3[] normals = new Vector3[vertexCount];
			Vector3 normal = new Vector3(0, 0, -1);
			for (int i = 0; i < vertexCount; i++)
				normals[i] = normal;
			(useMesh1 ? mesh2 : mesh1).vertices = vertices; // Set other mesh vertices.
			mesh1.normals = normals;
			mesh2.normals = normals;

			if (calculateTangents) {
				Vector4[] tangents = new Vector4[vertexCount];
				Vector3 tangent = new Vector3(0, 0, 1);
				for (int i = 0; i < vertexCount; i++)
					tangents[i] = tangent;
				mesh1.tangents = tangents;
				mesh2.tangents = tangents;
			}
		}

		// Update previous state
		ExposedList<int> attachmentsTriangleCountCurrentMesh = 
			useMesh1 ? 
			lastState.attachmentsTriangleCountMesh1 : 
			lastState.attachmentsTriangleCountMesh2;
		ExposedList<LastState.AddSubmeshArguments> addSubmeshArgumentsCurrentMesh = 
			useMesh1 ? 
			lastState.addSubmeshArgumentsMesh1 : 
			lastState.addSubmeshArgumentsMesh2;

		attachmentsTriangleCountCurrentMesh.GrowIfNeeded(attachmentsTriangleCountTemp.Capacity);
		attachmentsTriangleCountCurrentMesh.Count = attachmentsTriangleCountTemp.Count;
		attachmentsTriangleCountTemp.CopyTo(attachmentsTriangleCountCurrentMesh.Items, 0);

		addSubmeshArgumentsCurrentMesh.GrowIfNeeded(addSubmeshArgumentsTemp.Count);
		addSubmeshArgumentsCurrentMesh.Count = addSubmeshArgumentsTemp.Count;
		addSubmeshArgumentsTemp.CopyTo(addSubmeshArgumentsCurrentMesh.Items);

		if (useMesh1) {
			lastState.frontFacingMesh1 = frontFacing;
			lastState.immutableTrianglesMesh1 = immutableTriangles;
		} else {
			lastState.frontFacingMesh2 = frontFacing;
			lastState.immutableTrianglesMesh2 = immutableTriangles;
		}

		useMesh1 = !useMesh1;
	}

	private bool MustUpdateMeshStructure(ExposedList<int> attachmentsTriangleCountTemp, ExposedList<LastState.AddSubmeshArguments> addSubmeshArgumentsTemp) {
		// Check if any mesh settings were changed
		bool mustUpdateMeshStructure =
			frontFacing != (useMesh1 ? lastState.frontFacingMesh1 : lastState.frontFacingMesh2) ||
			immutableTriangles != (useMesh1 ? lastState.immutableTrianglesMesh1 : lastState.immutableTrianglesMesh2);
#if UNITY_EDITOR
		mustUpdateMeshStructure |= !Application.isPlaying;
#endif

		if (mustUpdateMeshStructure)
			return true;

		// Check if any attachments were enabled/disabled
		// or submesh structures has changed
		ExposedList<int> attachmentsTriangleCountCurrentMesh = 
			useMesh1 ? 
			lastState.attachmentsTriangleCountMesh1 : 
			lastState.attachmentsTriangleCountMesh2;
		ExposedList<LastState.AddSubmeshArguments> addSubmeshArgumentsCurrentMesh = 
			useMesh1 ? 
			lastState.addSubmeshArgumentsMesh1 : 
			lastState.addSubmeshArgumentsMesh2;

		// Check attachments
		int attachmentCount = attachmentsTriangleCountTemp.Count;
		if (attachmentsTriangleCountCurrentMesh.Count != attachmentCount) {
			mustUpdateMeshStructure = true;
		} else {
			for (int i = 0; i < attachmentCount; i++) {
				if (attachmentsTriangleCountCurrentMesh.Items[i] != attachmentsTriangleCountTemp.Items[i]) {
					mustUpdateMeshStructure = true;
					break;
				}
			}
		}

		if (mustUpdateMeshStructure)
			return true;

		// Check submeshes
		int submeshCount = addSubmeshArgumentsTemp.Count;
		if (addSubmeshArgumentsCurrentMesh.Count != submeshCount) {
			mustUpdateMeshStructure = true;
		} else {
			for (int i = 0; i < submeshCount; i++) {
				if (!addSubmeshArgumentsCurrentMesh.Items[i].Equals(addSubmeshArgumentsTemp.Items[i])) {
					mustUpdateMeshStructure = true;
					break;
				}
			}
		}

		return mustUpdateMeshStructure;
	}

	/** Stores vertices and triangles for a single material. */
	private void AddSubmesh (Material material, int startSlot, int endSlot, int triangleCount, int firstVertex, bool lastSubmesh) {
		int submeshIndex = submeshMaterials.Count;
		submeshMaterials.Add(material);

		if (submeshes.Count <= submeshIndex)
			submeshes.Add(new Submesh());
		else if (immutableTriangles)
			return;

		Submesh submesh = submeshes.Items[submeshIndex];

		int[] triangles = submesh.triangles;
		int trianglesCapacity = triangles.Length;
		if (lastSubmesh && trianglesCapacity > triangleCount) {
			// Last submesh may have more triangles than required, so zero triangles to the end.
			for (int i = triangleCount; i < trianglesCapacity; i++)
				triangles[i] = 0;
			submesh.triangleCount = triangleCount;
		} else if (trianglesCapacity != triangleCount) {
			// Reallocate triangles when not the exact size needed.
			submesh.triangles = triangles = new int[triangleCount];
			submesh.triangleCount = 0;
		}

		if (!renderMeshes && !frontFacing) {
			// Use stored triangles if possible.
			if (submesh.firstVertex != firstVertex || submesh.triangleCount < triangleCount) {
				submesh.triangleCount = triangleCount;
				submesh.firstVertex = firstVertex;
				int drawOrderIndex = 0;
				for (int i = 0; i < triangleCount; i += 6, firstVertex += 4, drawOrderIndex++) {
					triangles[i] = firstVertex;
					triangles[i + 1] = firstVertex + 2;
					triangles[i + 2] = firstVertex + 1;
					triangles[i + 3] = firstVertex + 2;
					triangles[i + 4] = firstVertex + 3;
					triangles[i + 5] = firstVertex + 1;
				}
			}
			return;
		}

		// Store triangles.
		ExposedList<Slot> drawOrder = skeleton.DrawOrder;
		for (int i = startSlot, triangleIndex = 0; i < endSlot; i++) {
			Slot slot = drawOrder.Items[i];
			Attachment attachment = slot.attachment;
			Bone bone = slot.bone;

			bool worldScaleXIsPositive = bone.worldScaleX >= 0f;
			bool worldScaleYIsPositive = bone.worldScaleY >= 0f;
			bool worldScaleIsSameSigns = (worldScaleXIsPositive && worldScaleYIsPositive) || 
										 (!worldScaleXIsPositive && !worldScaleYIsPositive);
			bool flip = frontFacing && ((bone.worldFlipX != bone.worldFlipY) != worldScaleIsSameSigns);

			if (attachment is RegionAttachment) {
				if (!flip) {
					triangles[triangleIndex] = firstVertex;
					triangles[triangleIndex + 1] = firstVertex + 2;
					triangles[triangleIndex + 2] = firstVertex + 1;
					triangles[triangleIndex + 3] = firstVertex + 2;
					triangles[triangleIndex + 4] = firstVertex + 3;
					triangles[triangleIndex + 5] = firstVertex + 1;
				} else {
					triangles[triangleIndex] = firstVertex + 1;
					triangles[triangleIndex + 1] = firstVertex + 2;
					triangles[triangleIndex + 2] = firstVertex;
					triangles[triangleIndex + 3] = firstVertex + 1;
					triangles[triangleIndex + 4] = firstVertex + 3;
					triangles[triangleIndex + 5] = firstVertex + 2;
				}

				triangleIndex += 6;
				firstVertex += 4;
				continue;
			}
			int[] attachmentTriangles;
			int attachmentVertexCount;
			MeshAttachment meshAttachment = attachment as MeshAttachment;
			if (meshAttachment != null) {
				attachmentVertexCount = meshAttachment.vertices.Length >> 1;
				attachmentTriangles = meshAttachment.triangles;
			} else {
				SkinnedMeshAttachment skinnedMeshAttachment = attachment as SkinnedMeshAttachment;
				if (skinnedMeshAttachment != null) {
					attachmentVertexCount = skinnedMeshAttachment.uvs.Length >> 1;
					attachmentTriangles = skinnedMeshAttachment.triangles;
				} else
					continue;
			}

			if (flip) {
				for (int ii = 0, nn = attachmentTriangles.Length; ii < nn; ii += 3, triangleIndex += 3) {
					triangles[triangleIndex + 2] = firstVertex + attachmentTriangles[ii];
					triangles[triangleIndex + 1] = firstVertex + attachmentTriangles[ii + 1];
					triangles[triangleIndex] = firstVertex + attachmentTriangles[ii + 2];
				}
			} else {
				for (int ii = 0, nn = attachmentTriangles.Length; ii < nn; ii++, triangleIndex++) {
					triangles[triangleIndex] = firstVertex + attachmentTriangles[ii];
				}
			}

			firstVertex += attachmentVertexCount;
		}
	}

#if UNITY_EDITOR
	void OnDrawGizmos () {
		// Make selection easier by drawing a clear gizmo over the skeleton.
		meshFilter = GetComponent<MeshFilter>();
		if (meshFilter == null) return;

		Mesh mesh = meshFilter.sharedMesh;
		if (mesh == null) return;

		Bounds meshBounds = mesh.bounds;
		Gizmos.color = Color.clear;
		Gizmos.matrix = transform.localToWorldMatrix;
		Gizmos.DrawCube(meshBounds.center, meshBounds.size);
	}
#endif

	private class LastState {
		public bool frontFacingMesh1;
		public bool frontFacingMesh2;
		public bool immutableTrianglesMesh1;
		public bool immutableTrianglesMesh2;
		public int vertexCount;
		public readonly ExposedList<int> attachmentsTriangleCountTemp = new ExposedList<int>();
		public readonly ExposedList<int> attachmentsTriangleCountMesh1 = new ExposedList<int>();
		public readonly ExposedList<int> attachmentsTriangleCountMesh2 = new ExposedList<int>();
		public readonly ExposedList<AddSubmeshArguments> addSubmeshArgumentsTemp = new ExposedList<AddSubmeshArguments>();
		public readonly ExposedList<AddSubmeshArguments> addSubmeshArgumentsMesh1 = new ExposedList<AddSubmeshArguments>();
		public readonly ExposedList<AddSubmeshArguments> addSubmeshArgumentsMesh2 = new ExposedList<AddSubmeshArguments>();

		public struct AddSubmeshArguments {
			public Material material;
			public int startSlot;
			public int endSlot;
			public int triangleCount;
			public int firstVertex;
			public bool lastSubmesh;

			public AddSubmeshArguments(Material material, int startSlot, int endSlot, int triangleCount, int firstVertex, bool lastSubmesh) {
				this.material = material;
				this.startSlot = startSlot;
				this.endSlot = endSlot;
				this.triangleCount = triangleCount;
				this.firstVertex = firstVertex;
				this.lastSubmesh = lastSubmesh;
			}

			public bool Equals(AddSubmeshArguments other) {
				return
					!ReferenceEquals(material, null) &&
					!ReferenceEquals(other.material, null) &&
					material.GetInstanceID() == other.material.GetInstanceID() &&
					startSlot == other.startSlot && 
					endSlot == other.endSlot && 
					triangleCount == other.triangleCount && 
					firstVertex == other.firstVertex;
			}
		}
	}
}

class Submesh {
	public int[] triangles = new int[0];
	public int triangleCount;
	public int firstVertex = -1;
}
