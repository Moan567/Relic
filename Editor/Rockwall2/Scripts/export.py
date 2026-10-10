import bpy, os, re, sys, traceback, json, mathutils

import addon_utils
addon_utils.enable('io_scene_fbx', default_set=False, persistent=False)

if not hasattr(bpy.ops.export_scene, 'fbx'):
    print('ERROR: io_scene_fbx addon could not be loaded.')
    print('In Blender 4.2+: Edit > Preferences > Get Extensions > install FBX format.')
    sys.exit(1)

def get_fcurves(action):
    if hasattr(action, 'layers'):
        for layer in action.layers:
            for strip in layer.strips:
                if hasattr(strip, 'channelbags'):
                    for cb in strip.channelbags:
                        yield from cb.fcurves
    elif hasattr(action, 'fcurves'):
        yield from action.fcurves


def _collect_objects_recursive(collection, seen=None):
    """All objects in `collection`, plus everything in its nested child
    collections. `seen` guards against (pathological) collection cycles."""
    if seen is None:
        seen = set()
    if collection.name in seen:
        return set()
    seen.add(collection.name)
    objs = set(collection.objects)
    for child in collection.children:
        objs |= _collect_objects_recursive(child, seen)
    return objs

def _find_layer_collection_path(layer_coll, name, path):
    path = path + [layer_coll]
    if layer_coll.collection.name == name:
        return path
    for child in layer_coll.children:
        result = _find_layer_collection_path(child, name, path)
        if result:
            return result
    return None

COLLECTION_FILTER = '__COLLECTION_FILTER__'

allowed_objects = None
if COLLECTION_FILTER:
    target_collection = bpy.data.collections.get(COLLECTION_FILTER)
    if target_collection is None:
        print(f"WARNING: collection '{COLLECTION_FILTER}' not found, exporting everything.")
    else:
        allowed_objects = _collect_objects_recursive(target_collection)
        print(f"Filtering to collection '{COLLECTION_FILTER}': {len(allowed_objects)} object(s).")

        path = _find_layer_collection_path(bpy.context.view_layer.layer_collection, COLLECTION_FILTER, [])
        if path:
            for layer_coll in path:
                layer_coll.exclude = False
        else:
            bpy.context.scene.collection.children.link(target_collection)

# Force a clean rest state before anything is exported. If the file was saved
# with the armature mid-pose (or with a stray action assigned), that pose is
# still baked into the evaluated mesh at export time even with bake_anim=False
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')

for arm_obj in [o for o in bpy.data.objects if o.type == 'ARMATURE']:
    if arm_obj.animation_data:
        arm_obj.animation_data.action = None
        for t in list(arm_obj.animation_data.nla_tracks):
            t.mute = True
    for pbone in arm_obj.pose.bones:
        pbone.matrix_basis = mathutils.Matrix.Identity(4)
bpy.context.view_layer.update()


all_armatures = [o for o in bpy.data.objects
                  if o.type == 'ARMATURE' and (allowed_objects is None or o in allowed_objects)]
if not all_armatures:
    print('No armature found, mesh-only export.')


def pick_armature(armatures):
    for action in bpy.data.actions:
        for slot in action.slots:
            for arm in armatures:
                if arm.name in slot.identifier:
                    return arm
    for arm in armatures:
        if arm.animation_data:
            return arm
    return max(armatures, key=lambda a: len(
        [o for o in bpy.data.objects if o.parent == a and o.type == 'MESH']))

def export_shapekey_anims(mesh_objects, out_dir, ARGS):
    scene_collection = bpy.context.scene.collection
    manifest = []

    for mesh_index, mesh_obj in enumerate(mesh_objects):
        sk = mesh_obj.data.shape_keys
        if not sk or len(sk.key_blocks) < 2:
            continue

        orig_values = {kb.name: kb.value for kb in sk.key_blocks}

        for kb in sk.key_blocks[1:]:
            safe_kb = re.sub(r'[/\\*?:"<>|]', '_', kb.name)
            fpath   = f"{out_dir}/shape_idx{mesh_index}_{safe_kb}.fbx"

            for other_kb in sk.key_blocks:
                other_kb.value = 0.0
            kb.value = 1.0
            bpy.context.view_layer.update()

            temp_obj   = None
            baked_mesh = None
            arm_mods   = []

            try:
                print(f"Exporting shape key: {mesh_obj.name} / {kb.name} (mesh index {mesh_index})")

                # Disable armature modifiers so the bake captures ONLY shape key
                # deformation
                for mod in mesh_obj.modifiers:
                    if mod.type == 'ARMATURE' and mod.show_viewport:
                        mod.show_viewport = False
                        arm_mods.append(mod)
                bpy.context.view_layer.update()

                depsgraph  = bpy.context.evaluated_depsgraph_get()
                eval_obj   = mesh_obj.evaluated_get(depsgraph)
                baked_mesh = bpy.data.meshes.new_from_object(
                    eval_obj,
                    preserve_all_data_layers=True,
                    depsgraph=depsgraph)

                temp_obj = bpy.data.objects.new(f"{mesh_obj.name}_BAKED", baked_mesh)
                temp_obj.matrix_world = mesh_obj.matrix_world.copy()
                scene_collection.objects.link(temp_obj)

                bpy.ops.object.select_all(action='DESELECT')
                temp_obj.select_set(True)
                bpy.context.view_layer.objects.active = temp_obj

                bpy.ops.export_scene.fbx(
                    filepath      = fpath,
                    use_selection = True,
                    bake_anim     = False,
                    **ARGS)

                manifest.append({
                    "mesh_index": mesh_index,
                    "mesh_name": mesh_obj.name,
                    "key": kb.name,
                    "file": os.path.basename(fpath),
                })

                print(f"Done: {kb.name}")

            except Exception:
                print(f"FAILED: {kb.name}")
                traceback.print_exc()

            finally:
                # Re-enable armature modifiers before anything else
                for mod in arm_mods:
                    mod.show_viewport = True

                if temp_obj is not None and temp_obj.name in bpy.data.objects:
                    bpy.data.objects.remove(temp_obj, do_unlink=True)
                if baked_mesh is not None and baked_mesh.name in bpy.data.meshes:
                    bpy.data.meshes.remove(baked_mesh)

                for name, val in orig_values.items():
                    sk.key_blocks[name].value = val
                bpy.context.view_layer.update()

    with open(f"{out_dir}/shapekeys_manifest.json", "w", encoding="utf-8") as f:
        json.dump(manifest, f)

armature = pick_armature(all_armatures) if all_armatures else None
if armature:
    print('Using armature: ' + armature.name)

if armature is not None:
    mesh_objects = [o for o in bpy.data.objects
                     if o.type == 'MESH' and o.find_armature() in (armature, None)
                     and (allowed_objects is None or o in allowed_objects)]
else:
    mesh_objects = [o for o in bpy.data.objects
                     if o.type == 'MESH' and (allowed_objects is None or o in allowed_objects)]
print('Mesh objects: ' + str([o.name for o in mesh_objects]))

if armature is not None:
    ARGS = dict(
        apply_unit_scale         = True,
        apply_scale_options      = 'FBX_SCALE_ALL',
        global_scale             = 0.1,
        axis_forward             = '-Z',
        axis_up                  = 'Y',
        add_leaf_bones           = False,
        use_armature_deform_only = True,
        mesh_smooth_type         = 'FACE',
        use_mesh_modifiers       = True,
    )
else:
    ARGS = dict(
        apply_unit_scale         = True,
        apply_scale_options      = 'FBX_SCALE_ALL',
        global_scale             = 0.1,
        axis_forward             = '-Z',
        axis_up                  = 'Y',
        bake_space_transform     = True,
        mesh_smooth_type         = 'FACE',
        use_mesh_modifiers       = True,
    )

keep = set(([armature] if armature else []) + mesh_objects)

bpy.ops.object.select_all(action='DESELECT')
for o in keep:
    o.select_set(True)
if armature is not None:
    bpy.context.view_layer.objects.active = armature
elif mesh_objects:
    bpy.context.view_layer.objects.active = mesh_objects[0]

try:
    print(f'Exporting mesh... ({len(keep)} object(s) selected)')
    bpy.ops.export_scene.fbx(
        filepath=r'__MESH_OUT__',
        use_selection=True,
        bake_anim=False,
        **ARGS)
    print('Mesh exported OK.')
except Exception:
    print('MESH EXPORT FAILED:')
    traceback.print_exc()
    sys.exit(1)

if armature is None:
    print('All done.')
    sys.exit(0)

print('Total actions: ' + str(len(bpy.data.actions)))

if armature.animation_data is None:
    armature.animation_data_create()

armature.animation_data.action = None
for t in list(armature.animation_data.nla_tracks):
    t.mute = True

# why the fuck is this so convoluted
export_shapekey_anims(mesh_objects, r'__OUT_DIR__', ARGS)

bpy.ops.object.select_all(action='DESELECT')
for o in keep:
    o.select_set(True)
bpy.context.view_layer.objects.active = armature

for action in bpy.data.actions:

    bone_chs = [fc for fc in get_fcurves(action)
                if fc.data_path.startswith('pose.bones')]
    if not bone_chs:
        print('Skipping ' + action.name + ' (no bone channels)')
        continue

    all_frames = [k.co[0] for fc in get_fcurves(action) for k in fc.keyframe_points]
    f_start = int(min(all_frames)) if all_frames else 0
    f_end   = int(max(all_frames)) if all_frames else 1
    bpy.context.scene.frame_start = f_start
    bpy.context.scene.frame_end   = f_end

    tmp_track = armature.animation_data.nla_tracks.new()
    tmp_track.name = '__export_tmp__'
    tmp_strip = tmp_track.strips.new(action.name, f_start, action)
    tmp_strip.action_frame_start = f_start
    tmp_strip.action_frame_end   = f_end
    if action.slots:
        try: tmp_strip.action_slot = action.slots[0]
        except: pass

    safe  = re.sub(r'[/\\*?:"<>|]', '_', action.name)
    fpath = r'__OUT_DIR__/' + 'anim_' + safe + '.fbx'

    try:
        print('Exporting: ' + action.name + ' [' + str(f_start) + '-' + str(f_end) + ']')
        bpy.ops.export_scene.fbx(
            filepath                  = fpath,
            use_selection             = True,
            bake_anim                 = True,
            bake_anim_use_all_actions = False,
            bake_anim_simplify_factor = 0.0,
            **ARGS)
        print('Done: ' + action.name)
    except Exception:
        print('FAILED: ' + action.name)
        traceback.print_exc()
    finally:
        armature.animation_data.nla_tracks.remove(tmp_track)

print('All done.')