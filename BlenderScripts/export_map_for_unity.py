import bpy
import json
import math
import os
import shutil
import sys
from mathutils import Vector


SOURCE_TEXTURE_DIR = os.path.join(os.path.dirname(bpy.data.filepath), "Textures", "Generated")
DEFAULT_OUTPUT_DIR = r"D:\FIT3040\Deinosavros\FIT3039-FIT3040_Deinosavros\Assets\Art\MapEnvironment"


def command_line_output_dir():
    if "--" not in sys.argv:
        return DEFAULT_OUTPUT_DIR
    arguments = sys.argv[sys.argv.index("--") + 1:]
    return os.path.abspath(arguments[0]) if arguments else DEFAULT_OUTPUT_DIR


def layer_collection_is_enabled(layer_collection):
    return not layer_collection.exclude and not layer_collection.collection.hide_render


def collect_render_objects(layer_collection, result):
    if not layer_collection_is_enabled(layer_collection):
        return
    for obj in layer_collection.collection.objects:
        if not obj.hide_render and obj.type in {"MESH", "CURVE", "SURFACE", "FONT"}:
            result.add(obj)
    for child in layer_collection.children:
        collect_render_objects(child, result)


def world_bounds(obj):
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    minimum = Vector((min(point.x for point in points), min(point.y for point in points), min(point.z for point in points)))
    maximum = Vector((max(point.x for point in points), max(point.y for point in points), max(point.z for point in points)))
    return minimum, maximum


def platform_anchors():
    anchors = {}
    mapping = {
        "Node_L01_01": 0,
        "Node_L02_01": 1,
        "Node_L02_02": 3,
        "Node_L03_01": 4,
        "Node_L03_02": 5,
        "Node_L03_03": 6,
        "Node_L04_01": 7,
        "Node_L04_02": 9,
        "Node_L05_01": 10,
        "Node_L05_02": 12,
    }
    for node_name, platform_index in mapping.items():
        obj = bpy.data.objects.get(f"Cohesive platform {platform_index:02d}")
        if obj is None:
            raise RuntimeError(f"Missing platform {platform_index:02d}")
        minimum, maximum = world_bounds(obj)
        anchors[node_name] = Vector(((minimum.x + maximum.x) * 0.5, (minimum.y + maximum.y) * 0.5, maximum.z + 0.12))
    anchors["Node_L06_01"] = Vector((14.0, 0.0, 1.25))
    return anchors


def create_anchor_objects(anchors, parent):
    collection = bpy.data.collections.new("Unity Map Anchors")
    bpy.context.scene.collection.children.link(collection)
    objects = []
    for node_name, position in anchors.items():
        anchor = bpy.data.objects.new(f"ANCHOR_{node_name}", None)
        anchor.empty_display_type = "PLAIN_AXES"
        anchor.empty_display_size = 0.6
        anchor.location = position
        anchor.parent = parent
        collection.objects.link(anchor)
        objects.append(anchor)
    return objects


def principled_node(material):
    if material is None or not material.use_nodes or material.node_tree is None:
        return None
    return next((node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"), None)


def socket_value(node, names, default):
    if node is None:
        return default
    for name in names:
        socket = node.inputs.get(name)
        if socket is not None:
            value = socket.default_value
            if hasattr(value, "__len__"):
                return [float(component) for component in value]
            return float(value)
    return default


def material_record(material):
    node = principled_node(material)
    base_color = socket_value(node, ["Base Color"], list(material.diffuse_color))
    emission = socket_value(node, ["Emission Color", "Emission"], [0.0, 0.0, 0.0, 1.0])
    emission_strength = socket_value(node, ["Emission Strength"], 0.0)
    roughness = socket_value(node, ["Roughness"], material.roughness)
    metallic = socket_value(node, ["Metallic"], material.metallic)
    alpha = socket_value(node, ["Alpha"], material.diffuse_color[3])
    lower_name = material.name.lower()
    transparent = any(term in lower_name for term in ("mist", "fog", "waterfall", "membrane", "chasmwater", "haze"))
    texture_id = material.get("texture_asset_id", "")
    if material.name == "Production Palm Bark":
        texture_id = "generated_palm_bark"
    return {
        "name": material.name,
        "baseColor": base_color,
        "roughness": roughness,
        "metallic": metallic,
        "emission": emission,
        "emissionStrength": emission_strength,
        "alpha": alpha,
        "transparent": transparent,
        "textureId": texture_id,
    }


def create_world_uv(mesh_object, texture_scale=0.32):
    mesh = mesh_object.data
    if len(mesh.polygons) == 0:
        return
    uv_layer = mesh.uv_layers.get("UVMap") or mesh.uv_layers.new(name="UVMap")
    normal_matrix = mesh_object.matrix_world.to_3x3()
    for polygon in mesh.polygons:
        normal = normal_matrix @ polygon.normal
        axis = max(range(3), key=lambda index: abs(normal[index]))
        for loop_index in polygon.loop_indices:
            vertex = mesh.vertices[mesh.loops[loop_index].vertex_index]
            position = mesh_object.matrix_world @ vertex.co
            if axis == 0:
                uv = (position.y * texture_scale, position.z * texture_scale)
            elif axis == 1:
                uv = (position.x * texture_scale, position.z * texture_scale)
            else:
                uv = (position.x * texture_scale, position.y * texture_scale)
            uv_layer.data[loop_index].uv = uv


def convert_to_mesh(objects):
    converted = []
    for obj in objects:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        if obj.type != "MESH" or len(obj.modifiers) > 0:
            bpy.ops.object.convert(target="MESH")
        create_world_uv(obj)
        converted.append(obj)
    return converted


def object_category(obj):
    lower_name = obj.name.lower()
    collection_names = {collection.name.lower() for collection in obj.users_collection}
    if "atmosphere" in collection_names or any(term in lower_name for term in ("chasm", "haze", "mist")):
        return "Atmosphere"
    if "platform" in lower_name:
        return "Platforms"
    if "route" in lower_name or "path" in lower_name:
        return "Routes"
    if any(term in lower_name for term in ("portal", "brazier", "flame")):
        return "Portal"
    if any(term in lower_name for term in ("chest", "sword", "scroll", "banner", "parchment")):
        return "Props"
    if any(term in lower_name for term in ("column", "arch", "ruin", "slab", "stair")):
        return "Ruins"
    if "vegetation" in collection_names or any(term in lower_name for term in ("shrub", "fern", "palm", "vine", "moss", "leaf", "root")):
        return "Vegetation"
    if any(term in lower_name for term in ("island", "cliff", "ground", "terrain", "rock")):
        return "Terrain"
    return "Other"


def create_export_hierarchy(objects):
    collection = bpy.data.collections.new("Unity Export Hierarchy")
    bpy.context.scene.collection.children.link(collection)
    root = bpy.data.objects.new("MapEnvironmentModel", None)
    collection.objects.link(root)
    groups = {}
    for name in ("Terrain", "Routes", "Platforms", "Portal", "Props", "Ruins", "Vegetation", "Atmosphere", "Other", "GameplayAnchors"):
        group = bpy.data.objects.new(name, None)
        group.parent = root
        collection.objects.link(group)
        groups[name] = group
    for obj in objects:
        matrix = obj.matrix_world.copy()
        obj.parent = groups[object_category(obj)]
        obj.matrix_world = matrix
    return root, groups


def combined_world_bounds(objects):
    bounds = [world_bounds(obj) for obj in objects]
    minimum = Vector((
        min(item[0].x for item in bounds),
        min(item[0].y for item in bounds),
        min(item[0].z for item in bounds),
    ))
    maximum = Vector((
        max(item[1].x for item in bounds),
        max(item[1].y for item in bounds),
        max(item[1].z for item in bounds),
    ))
    return minimum, maximum


def export_scene(output_dir):
    os.makedirs(output_dir, exist_ok=True)
    texture_dir = os.path.join(output_dir, "Textures")
    os.makedirs(texture_dir, exist_ok=True)
    for filename in sorted(os.listdir(SOURCE_TEXTURE_DIR)):
        if filename.lower().endswith((".png", ".jpg", ".jpeg", ".tga")):
            shutil.copy2(os.path.join(SOURCE_TEXTURE_DIR, filename), os.path.join(texture_dir, filename))

    anchors = platform_anchors()
    render_objects = set()
    collect_render_objects(bpy.context.view_layer.layer_collection, render_objects)
    render_objects = sorted(render_objects, key=lambda obj: obj.name)
    if not render_objects:
        raise RuntimeError("No renderable map objects found")

    used_materials = {}
    for obj in render_objects:
        for material in getattr(obj.data, "materials", []):
            if material is not None:
                used_materials[material.name] = material

    converted = convert_to_mesh(render_objects)
    hierarchy_root, hierarchy_groups = create_export_hierarchy(converted)
    anchor_objects = create_anchor_objects(anchors, hierarchy_groups["GameplayAnchors"])

    bpy.ops.object.select_all(action="DESELECT")
    export_objects = converted + anchor_objects + list(hierarchy_groups.values()) + [hierarchy_root]
    for obj in export_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = converted[0]

    fbx_path = os.path.join(output_dir, "MapEnvironment.fbx")
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        use_mesh_modifiers=True,
        use_triangles=True,
        add_leaf_bones=False,
        path_mode="COPY",
        embed_textures=False,
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        bake_space_transform=False,
    )

    minimum, maximum = combined_world_bounds(converted)
    manifest = {
        "sourceBlend": bpy.data.filepath,
        "fbx": fbx_path,
        "objectCountBeforeJoin": len(render_objects),
        "bounds": {
            "minimum": list(minimum),
            "maximum": list(maximum),
        },
        "anchors": {name: list(position) for name, position in anchors.items()},
        "materials": [material_record(used_materials[name]) for name in sorted(used_materials)],
    }
    manifest_path = os.path.join(output_dir, "MapEnvironmentManifest.json")
    with open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)

    print(json.dumps({
        "fbx": fbx_path,
        "manifest": manifest_path,
        "objects": len(render_objects),
        "materials": len(used_materials),
        "anchors": len(anchors),
        "hierarchyGroups": len(hierarchy_groups),
    }, indent=2))


export_scene(command_line_output_dir())
