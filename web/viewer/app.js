import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

const container = document.querySelector('#scene');
const ui = Object.fromEntries([
  'connection','mode','execution','step','order','part','progress','oee',
  'x-actual','x-target','x-error','y-actual','y-target','y-error','interlocks','integration','alarms'
].map(id => [id.replaceAll('-', ''), document.getElementById(id)]));

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x07101a);
scene.fog = new THREE.Fog(0x07101a, 14, 34);
const camera = new THREE.PerspectiveCamera(42, 1, 0.1, 100);
const defaultCamera = new THREE.Vector3(10, 7.5, 11.5);
camera.position.copy(defaultCamera);
const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.outputColorSpace = THREE.SRGBColorSpace;
container.appendChild(renderer.domElement);

const controls = new OrbitControls(camera, renderer.domElement);
controls.target.set(0, 0.3, 0);
controls.enableDamping = true;
controls.maxPolarAngle = Math.PI * 0.48;
controls.minDistance = 6;
controls.maxDistance = 22;

scene.add(new THREE.HemisphereLight(0x9cc9ff, 0x101820, 1.7));
const key = new THREE.DirectionalLight(0xffffff, 2.4);
key.position.set(6, 11, 7); key.castShadow = true; scene.add(key);
const fill = new THREE.DirectionalLight(0x4a9fdd, 1.0);
fill.position.set(-8, 4, -5); scene.add(fill);

const materials = {
  frame: new THREE.MeshStandardMaterial({ color: 0x5d6f82, metalness: .65, roughness: .35 }),
  rail: new THREE.MeshStandardMaterial({ color: 0xa9b5c1, metalness: .8, roughness: .25 }),
  carriage: new THREE.MeshStandardMaterial({ color: 0x188bd1, metalness: .35, roughness: .35 }),
  carriageFault: new THREE.MeshStandardMaterial({ color: 0xd9414a, emissive: 0x42080b, metalness: .25 }),
  tool: new THREE.MeshStandardMaterial({ color: 0x21b387, metalness: .25, roughness: .4 }),
  safety: new THREE.MeshStandardMaterial({ color: 0x2b8fb7, transparent: true, opacity: .16, side: THREE.DoubleSide }),
  safetyFault: new THREE.MeshStandardMaterial({ color: 0xd9414a, transparent: true, opacity: .35, side: THREE.DoubleSide }),
  pick: new THREE.MeshStandardMaterial({ color: 0x2389bd, roughness: .5 }),
  inspect: new THREE.MeshStandardMaterial({ color: 0x8c5ac7, roughness: .5 }),
  place: new THREE.MeshStandardMaterial({ color: 0x32a26d, roughness: .5 }),
  part: new THREE.MeshStandardMaterial({ color: 0xf2a72c, roughness: .45 }),
  target: new THREE.MeshStandardMaterial({ color: 0x58dcff, transparent: true, opacity: .22, wireframe: true }),
  floor: new THREE.MeshStandardMaterial({ color: 0x101a25, roughness: .9 })
};
function box(name, size, position, material, parent = scene) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(...size), material);
  mesh.name = name; mesh.position.set(...position); mesh.castShadow = true; mesh.receiveShadow = true; parent.add(mesh); return mesh;
}
function label(text, position) {
  const canvas = document.createElement('canvas'); canvas.width = 512; canvas.height = 128;
  const context = canvas.getContext('2d'); context.fillStyle = 'rgba(8,15,24,.82)'; context.fillRect(0, 0, 512, 128);
  context.strokeStyle = '#7091ae'; context.strokeRect(2, 2, 508, 124); context.fillStyle = '#e8f2fa'; context.font = '700 42px system-ui';
  context.textAlign = 'center'; context.textBaseline = 'middle'; context.fillText(text, 256, 64);
  const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: new THREE.CanvasTexture(canvas), transparent: true }));
  sprite.position.set(...position); sprite.scale.set(2.1, .52, 1); scene.add(sprite);
}

box('floor', [8.5,.18,6.2], [0,-1.32,0], materials.floor);
for (const x of [-3.8,3.8]) for (const z of [-2.7,2.7]) box('post',[.16,3.9,.16],[x,.58,z],materials.frame);
box('top-front',[7.7,.16,.16],[0,2.48,-2.7],materials.frame); box('top-back',[7.7,.16,.16],[0,2.48,2.7],materials.frame);
box('top-left',[.16,.16,5.5],[-3.8,2.48,0],materials.frame); box('top-right',[.16,.16,5.5],[3.8,2.48,0],materials.frame);
box('fence-back',[7.3,3.4,.04],[0,.55,-2.72],materials.safety);
const door = box('door',[.05,3.2,2.0],[3.83,.48,.55],materials.safety);
box('rail-front',[6.8,.17,.22],[0,1.68,-1.72],materials.rail); box('rail-back',[6.8,.17,.22],[0,1.68,1.72],materials.rail);
box('pick-station',[1.15,.65,1.15],[2.45,-.9,-1.18],materials.pick); label('PICK',[2.45,-.2,-1.18]);
box('inspect-station',[1.15,.65,1.15],[.62,-.9,1.42],materials.inspect); label('INSPECT',[.62,-.2,1.42]);
box('place-station',[1.15,.65,1.15],[-2.15,-.9,.64],materials.place); label('PLACE',[-2.15,-.2,.64]);

const gantry = new THREE.Group(); scene.add(gantry);
const bridge = box('gantry-bridge',[.42,.32,3.85],[0,1.86,0],materials.carriage,gantry);
const yCarriage = new THREE.Group(); gantry.add(yCarriage);
const head = box('tool-head',[.66,.72,.66],[0,1.36,0],materials.tool,yCarriage);
box('tool-shaft',[.2,.82,.2],[0,.66,0],materials.rail,yCarriage);
box('finger-left',[.11,.42,.28],[-.19,.18,0],materials.part,yCarriage);
box('finger-right',[.11,.42,.28],[.19,.18,0],materials.part,yCarriage);
const workpiece = box('workpiece',[.48,.3,.48],[0,-.12,0],materials.part,yCarriage);
const targetGhost = box('target-ghost',[.72,.78,.72],[0,1.36,0],materials.target);
targetGhost.visible = true;

const state = { snapshot:null, connected:false, replay:[], replayStart:performance.now(), displayedX:0, displayedY:0, targetX:0, targetY:0 };
const toWorldX = value => value * 3.05;
const toWorldZ = value => (value - .45) * 3.25;

function updateScene(snapshot) {
  state.targetX = snapshot.xAxis.actualPosition;
  state.targetY = snapshot.yAxis.actualPosition;
  const faulted = snapshot.executionState === 'Faulted' || snapshot.executionState === 'RecoveryRequired';
  bridge.material = faulted ? materials.carriageFault : materials.carriage;
  head.material = faulted ? materials.carriageFault : materials.tool;
  door.position.x = snapshot.interlocks.guardClosed ? 3.83 : 4.55;
  door.rotation.y = snapshot.interlocks.guardClosed ? 0 : -.55;
  door.material = snapshot.interlocks.guardClosed ? materials.safety : materials.safetyFault;
  targetGhost.position.set(toWorldX(snapshot.xAxis.targetPosition), 1.36, toWorldZ(snapshot.yAxis.targetPosition));
  workpiece.visible = Boolean(snapshot.activePart);
  if (snapshot.activePart && !snapshot.activePart.attachedToGripper) {
    workpiece.removeFromParent(); scene.add(workpiece); workpiece.position.set(-2.15,-.38,.64);
  } else if (snapshot.activePart && workpiece.parent !== yCarriage) {
    workpiece.removeFromParent(); yCarriage.add(workpiece); workpiece.position.set(0,-.12,0);
  }
}

function updateUi(s) {
  ui.mode.textContent=s.mode; ui.execution.textContent=s.executionState; ui.step.textContent=s.productionStep;
  ui.order.textContent=s.production.activeOrder?.orderId ?? '--'; ui.part.textContent=s.activePart?.partId ?? '--';
  const order=s.production.activeOrder; ui.progress.textContent=order?`${order.completedQuantity} / ${order.targetQuantity}`:'0 / 0';
  ui.oee.textContent=`${(s.production.oee*100).toFixed(1)}%`;
  for (const [prefix,axis] of [['x',s.xAxis],['y',s.yAxis]]) {
    ui[`${prefix}actual`].textContent=axis.actualPosition.toFixed(3);
    ui[`${prefix}target`].textContent=axis.targetPosition.toFixed(3);
    ui[`${prefix}error`].textContent=axis.followingError.toFixed(3);
  }
  if (s.interlocks.motionPermitted) { ui.interlocks.textContent='Motion permitted'; ui.interlocks.className='ok'; }
  else { ui.interlocks.textContent=s.interlocks.blockingReasons.join(' · '); ui.interlocks.className='error'; }
  ui.integration.textContent=`MES ${s.integration.mesHealth} · Outbox ${s.integration.outboxPending} · OPC UA ${s.integration.opcUaEnabled?'on':'off'}`;
  ui.alarms.innerHTML=s.activeAlarms.length?s.activeAlarms.map(a=>`<div class="alarm"><strong>${escapeHtml(a.code)}</strong> — ${escapeHtml(a.message)}</div>`).join(''):'No active alarms';
  updateScene(s);
}
function escapeHtml(value){return String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]));}

async function loadReplay(){try{state.replay=await (await fetch('sample-replay.json')).json();}catch{state.replay=[];}}
function replayTick(){
  if(!state.connected && state.replay.length){
    const frame=state.replay[Math.floor(((performance.now()-state.replayStart)/100)*1)%state.replay.length];
    state.snapshot=frame; updateUi(frame); ui.connection.textContent='RECORDED DEMO'; ui.connection.className='badge warning';
  }
  setTimeout(replayTick,100);
}
function connect(){
  const protocol=location.protocol==='https:'?'wss':'ws';
  const socket=new WebSocket(`${protocol}://${location.host}/ws/state`);
  socket.addEventListener('open',()=>{state.connected=true;ui.connection.textContent='LIVE';ui.connection.className='badge ok';});
  socket.addEventListener('message',event=>{const snapshot=JSON.parse(event.data);state.snapshot=snapshot;updateUi(snapshot);});
  socket.addEventListener('close',()=>{state.connected=false;state.replayStart=performance.now();ui.connection.textContent='RECONNECTING';ui.connection.className='badge warning';setTimeout(connect,1500);});
  socket.addEventListener('error',()=>socket.close());
}

function resize(){const w=container.clientWidth,h=container.clientHeight;camera.aspect=w/h;camera.updateProjectionMatrix();renderer.setSize(w,h,false);}
function animate(){
  state.displayedX += (state.targetX-state.displayedX)*.14; state.displayedY += (state.targetY-state.displayedY)*.14;
  gantry.position.x=toWorldX(state.displayedX); yCarriage.position.z=toWorldZ(state.displayedY);
  controls.update(); renderer.render(scene,camera); requestAnimationFrame(animate);
}
window.addEventListener('resize',resize);
document.getElementById('reset-camera').addEventListener('click',()=>{camera.position.copy(defaultCamera);controls.target.set(0,.3,0);controls.update();});
document.getElementById('toggle-target').addEventListener('click',()=>{targetGhost.visible=!targetGhost.visible;});

resize(); await loadReplay(); connect(); replayTick(); animate();
