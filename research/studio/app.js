const state={templates:[],manifest:null,job:null,timer:null};
const el=id=>document.getElementById(id);
async function loadTemplates(){
  const response=await fetch('/api/templates'); state.templates=await response.json();
  el('templateCards').innerHTML='';
  state.templates.forEach((template,index)=>{
    const button=document.createElement('button'); button.className='template-card';
    button.innerHTML=`<strong>${template.name}</strong><p>${template.manifest.description||'Reproducible dynamic benchmark template.'}</p>`;
    button.onclick=()=>selectTemplate(template,button); el('templateCards').appendChild(button);
    if(index===0) selectTemplate(template,button);
  });
}
function selectTemplate(template,button){
  document.querySelectorAll('.template-card').forEach(x=>x.classList.remove('active')); button.classList.add('active');
  state.manifest=structuredClone(template.manifest); applyControls(); renderManifest(); renderWorkflow();
}
function applyControls(){
  el('experimentName').value=state.manifest.name||state.manifest.experiment_id;
  el('episodes').value=state.manifest.episodes||1; el('duration').value=state.manifest.environment.duration_s;
  el('seed').value=state.manifest.base_seed||1001; el('window').value=state.manifest.datasets?.window_s||1;
  const domains=new Set((state.manifest.scenarios||[]).map(x=>x.domain));
  el('scenarioMode').value=domains.has('machine')&&domains.has('network')?'combined':domains.has('machine')?'machine':domains.has('network')?'network':'normal';
}
function syncControls(){
  if(!state.manifest)return;
  state.manifest.name=el('experimentName').value; state.manifest.episodes=Number(el('episodes').value);
  state.manifest.base_seed=Number(el('seed').value); state.manifest.environment.duration_s=Number(el('duration').value);
  state.manifest.datasets=state.manifest.datasets||{}; state.manifest.datasets.window_s=Number(el('window').value);
  const mode=el('scenarioMode').value; const scenarios=[];
  if(mode==='machine'||mode==='combined') scenarios.push(machineScenario(el('machineFault').value));
  if(mode==='network'||mode==='combined') scenarios.push(networkScenario());
  state.manifest.scenarios=scenarios; renderManifest(); renderWorkflow();
}
function machineScenario(type){
  const map={
    increased_friction:{category:'mechanical',component:'x_axis',progression:'gradual',magnitude:.7},
    encoder_drift:{category:'sensor_feedback',component:'x_axis_encoder',progression:'gradual',magnitude:.12},
    drive_derating:{category:'actuator_drive',component:'x_axis_drive',progression:'abrupt',magnitude:.55},
    failed_pick:{category:'process_tooling',component:'gripper',progression:'intermittent',magnitude:.65}
  }[type];
  return {id:`machine-${type}`,domain:'machine',category:map.category,type,component:map.component,activation_time_s:12,duration_s:22,progression:map.progression,magnitude:map.magnitude,severity:'major'};
}
function networkScenario(){return {id:'network-delay',domain:'network',category:'timing',type:'message_delay',component:'state_channel',activation_time_s:18,duration_s:14,progression:'abrupt',magnitude:45,severity:'minor'};}
function renderManifest(){el('manifestEditor').value=JSON.stringify(state.manifest,null,2);}
function renderWorkflow(){
  const mode=el('scenarioMode').value; el('workflowMode').textContent=mode.replace('_',' ')+' operation';
  const fault=el('faultNode'); fault.className='workflow-node n5 fault-card';
  if(mode==='normal')fault.querySelector('span').textContent='Normal baseline';
  if(mode==='machine'){fault.classList.add('active-machine');fault.querySelector('span').textContent=el('machineFault').value.replaceAll('_',' ')}
  if(mode==='network'){fault.classList.add('active-network');fault.querySelector('span').textContent='Network delay'}
  if(mode==='combined'){fault.classList.add('active-machine','active-network');fault.querySelector('span').textContent='Machine + network'}
}
async function validateManifest(){
  try{state.manifest=JSON.parse(el('manifestEditor').value);const r=await fetch('/api/validate',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify(state.manifest)});if(!r.ok)throw new Error((await r.json()).detail);message('Manifest is valid and reproducible.',true)}catch(error){message(error.message,false)}
}
async function runExperiment(){
  await validateManifest(); if(!state.manifest)return; el('runButton').disabled=true; animate(true);
  const r=await fetch('/api/experiments',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify(state.manifest)});
  if(!r.ok){message('Unable to start experiment.',false);el('runButton').disabled=false;return}
  state.job=(await r.json()).job_id; poll();
}
async function poll(){
  const r=await fetch(`/api/experiments/${state.job}`); const job=await r.json(); renderMetrics(job);
  if(job.status==='completed'||job.status==='failed'){el('runButton').disabled=false;animate(false);return}
  setTimeout(poll,600);
}
function renderMetrics(job){
  const m=job.metrics||{}; const rows={Status:job.status,Episodes:m.episodes??'—','Completed cycles':m.completed_cycles??'—','Telemetry rows':m.telemetry_rows??'—','Network messages':m.network_messages??'—','Dataset windows':m.dataset_windows??'—','Max following error':m.max_following_error?Number(m.max_following_error).toFixed(4):'—'};
  el('metrics').innerHTML=Object.entries(rows).map(([k,v])=>`<dt>${k}</dt><dd>${v}</dd>`).join(''); el('bundlePath').textContent=job.bundle?`Bundle: ${job.bundle}`:(job.error||'');
}
function message(text,good){el('validationMessage').textContent=text;el('validationMessage').className='message '+(good?'good':'bad')}
function animate(running){
  clearInterval(state.timer); if(!running){el('timelineBar').style.width='100%';return}
  let p=0;state.timer=setInterval(()=>{p=(p+4)%100;el('timelineBar').style.width=p+'%';el('carriage').style.left=(12+70*Math.abs(Math.sin(p/24)))+'%';el('part').style.left=(18+62*Math.abs(Math.sin(p/28)))+'%'},180);
}
['experimentName','episodes','duration','seed','window','scenarioMode','machineFault'].forEach(id=>el(id).addEventListener('change',syncControls));
el('manifestEditor').addEventListener('change',()=>{try{state.manifest=JSON.parse(el('manifestEditor').value);applyControls();renderWorkflow()}catch{}});
el('validateButton').onclick=validateManifest;el('runButton').onclick=runExperiment;el('refreshTemplates').onclick=loadTemplates;
loadTemplates().catch(error=>message(error.message,false));
