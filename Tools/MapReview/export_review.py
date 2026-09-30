"""Export only original review assets; retain per-object editability and anchor names."""
import bpy
import bmesh
import json
import sys
from pathlib import Path
from mathutils import Vector

out=Path(sys.argv[sys.argv.index('--')+1]).resolve()
out.mkdir(parents=True,exist_ok=True)
full_scene=bpy.data.scenes.get('V2 Environment Review')
full_model_name='RuinsV3Review.fbx' if Path(bpy.data.filepath).stem.endswith('_v3') else 'RuinsV2Review.fbx'
manifest={'models':[],'materials':[], 'stage':'full_geometry_pending_final_visual_acceptance' if full_scene else 'representative_sample_pending_game_camera_gate',
          'sourceBlend':bpy.data.filepath, 'axisConversion':'Blender Z-up to FBX Y-up, forward -Z',
          'portalApertureRadius':2.09,'vertexColorEncoding':'LINEAR; authored blend weights are not sRGB colors'}
for scene_name,file_name,study in [('Map_Rebuild_20260924','RuinsSourceReview.fbx',False),
                                    ('V2 Representative Study','RuinsSample.fbx',True),
                                    ('V2 Portal Connection Study','RuinsPortalSample.fbx',True),
                                    ('V2 Environment Review',full_model_name,False)]:
    scene=bpy.data.scenes.get(scene_name)
    if scene is None:continue
    bpy.context.window.scene=scene
    renamed=[]
    name_mapping=[]
    if scene_name=='V2 Environment Review':
        for obj in list(scene.objects):
            canonical=obj.get('canonical_name')
            if not canonical:continue
            other=bpy.data.objects.get(canonical)
            if other is not None and other!=obj:
                renamed.append((other,other.name));other.name='Delivery source '+canonical
            native_name=obj.name
            renamed.append((obj,native_name));obj.name=canonical
            name_mapping.append({'nativeName':native_name,'deliveryName':obj.name})
    for obj in scene.objects:obj.select_set(False)
    selection=[]
    for obj in scene.objects:
        if obj.type not in {'MESH','EMPTY'}:continue
        if obj.hide_render or any(c.hide_render for c in obj.users_collection):continue
        if any('Props' in c.name or ('Background' in c.name and scene_name!='V2 Environment Review') for c in obj.users_collection):continue
        obj.hide_set(False);obj.select_set(True);selection.append(obj)
    checks=[]
    for obj in selection:
        if obj.type!='MESH':continue
        # Triangulation is an export-only modifier; native construction stays editable.
        tri=obj.modifiers.new('Review delivery triangulation','TRIANGULATE')
        tri.quad_method='BEAUTY';tri.ngon_method='BEAUTY'
        evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh=evaluated.to_mesh()
        bm=bmesh.new();bm.from_mesh(mesh)
        degenerate=sum(face.calc_area()<1e-10 for face in bm.faces)
        boundaries=sum(edge.is_boundary for edge in bm.edges)
        nonmanifold=sum(not edge.is_manifold for edge in bm.edges)
        checks.append({'name':obj.name,'triangles':len(mesh.polygons),
                       'degenerateFaces':degenerate,'boundaryEdges':boundaries,'nonManifoldEdges':nonmanifold})
        bm.free();evaluated.to_mesh_clear()
        strict=study or bool(obj.get('original_geometry',False))
        if strict and degenerate:raise RuntimeError('Degenerate export surface: '+obj.name)
        if strict and nonmanifold and not obj.get('intentional_open_surface',False):raise RuntimeError('Open export surface: '+obj.name)
    bpy.ops.export_scene.fbx(filepath=str(out/file_name),use_selection=True,object_types={'MESH','EMPTY'},
        axis_forward='-Z',axis_up='Y',apply_unit_scale=True,global_scale=1,
        bake_space_transform=False,use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,
        bake_anim=False,path_mode='AUTO',use_custom_props=True,colors_type='LINEAR')
    manifest['models'].append({'path':file_name,'scene':scene_name,
        'meshCount':sum(o.type=='MESH' for o in selection),
        'sourceMeshPolygons':sum(len(o.data.polygons) for o in selection if o.type=='MESH'),
        'geometryChecks':checks,
        'nameMapping':name_mapping,
        'objects':[{'name':o.name, 'type':o.type,
                    'reviewBackground':bool(o.get('review_background',False)),
                    'runtimeFoliage':bool(o.get('runtime_foliage',False)),
                    'intentionalOpenSurface':bool(o.get('intentional_open_surface',False)),
                    'uvChannels':[layer.name for layer in o.data.uv_layers] if o.type=='MESH' else [],
                    'colorChannels':[layer.name for layer in o.data.color_attributes] if o.type=='MESH' else [],
                    'materials':[s.material.name if s.material else None for s in o.material_slots],
                    'position':list(o.matrix_world.translation)} for o in selection],
        'anchors':[{'name':o.name,'position':list(o.matrix_world.translation)} for o in selection if o.name.startswith('ANCHOR_')],
        'terraces':[{'name':o.name,'position':list(o.matrix_world.translation)} for o in selection if o.name.startswith('Node terrace')]})
    for obj,original_name in reversed(renamed):obj.name=original_name
for mat in bpy.data.materials:
    bsdf=mat.node_tree.nodes.get('Principled BSDF') if mat.use_nodes and mat.node_tree else None
    roughness=bsdf.inputs['Roughness'].default_value if bsdf else mat.roughness
    manifest['materials'].append({'name':mat.name,'color':list(mat.diffuse_color), 'roughness':roughness,
                                 'metallic':bsdf.inputs['Metallic'].default_value if bsdf else mat.metallic,
                                 'originalAtlas':bool(mat.get('original_atlas',False)),
                                 'baseMap':mat.get('base_map',''),'normalMap':mat.get('normal_map',''),
                                 'roughnessMap':mat.get('roughness_map',''),'alphaClip':mat.get('alpha_clip',0.0)})
(out/'RuinsManifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
