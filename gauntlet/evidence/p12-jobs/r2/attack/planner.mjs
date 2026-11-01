// Real product: a user who holds only jobs.schedules.create and jobs.schedules.read schedules the clean-up kind,
// whose own run permission is jobs.schedules.update; the worker then runs it as system work.
const BASE='http://localhost:21250';
const H={'content-type':'application/json','X-Erp-Request':'1'};
async function signIn(email,password='Demo-Pass-2026'){const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:H,body:JSON.stringify({email,password,issueToken:true})});const b=await r.json();if(!b.token)throw new Error(email+' '+JSON.stringify(b));return b.token;}
const call=async(t,m,p,body)=>{const r=await fetch(BASE+p,{method:m,headers:{...H,Authorization:'Bearer '+t},body:body?JSON.stringify(body):undefined});const tx=await r.text();let j;try{j=JSON.parse(tx)}catch{j=tx}return [r.status,j];};
const admin=await signIn('admin@alnoor.example');
const [rs,role]=await call(admin,'POST','/api/identity/roles',{nameEn:'Schedule planner (critic)',nameAr:'مخطط الجداول (ناقد)',permissions:['jobs.schedules.create','jobs.schedules.read']});
console.log('role',rs,role.id??role);
const email='planner.critic@alnoor.example';
const [us,user]=await call(admin,'POST','/api/identity/users',{email,displayName:'Planner Critic',language:'en',password:'Planner-Pass-2026x',mustChangePassword:false,roleIds:[role.id]});
console.log('user',us,user.id??JSON.stringify(user).slice(0,300));
await new Promise(r=>setTimeout(r,2500));
const pl=await signIn(email,'Planner-Pass-2026x');
console.log('planner start cleanup:', (await call(pl,'POST','/api/jobs/jobs',{type:'jobs.cleanup',payload:{retentionDays:30}}))[0]);
console.log('planner list jobs:', (await call(pl,'GET','/api/jobs/jobs?take=1'))[0]);
const [ss,sch]=await call(pl,'POST','/api/jobs/schedules',{scheduleName:'Critic every-minute clean-up',type:'jobs.cleanup',cron:'* * * * *',payload:{retentionDays:30}});
console.log('planner schedule cleanup:', ss, JSON.stringify(sch).slice(0,400));
const before=(await call(admin,'GET','/api/jobs/jobs?take=1'))[1].total;
console.log('jobs total before', before);
for (let i=0;i<20;i++){await new Promise(r=>setTimeout(r,6000)); const [_,s]=await call(admin,'GET','/api/jobs/schedules/'+sch.id); if(s.lastJobId){const [__,j]=await call(admin,'GET','/api/jobs/jobs/'+s.lastJobId); if(['succeeded','failed'].includes(j.status)){console.log('job queued by the schedule:', j.id, j.type, j.status, j.source, 'result', JSON.stringify(j.result), 'error', j.errorEn); break;} else console.log('waiting', j.status);} else console.log('no job yet');}
console.log('jobs total after', (await call(admin,'GET','/api/jobs/jobs?take=1'))[1].total);
// switch the schedule off again (admin)
const [_,s]=await call(admin,'GET','/api/jobs/schedules/'+sch.id);
console.log('switch off', (await call(admin,'PUT','/api/jobs/schedules/'+sch.id,{scheduleName:s.scheduleName,cron:s.cron,isEnabled:false,version:s.version}))[0]);
