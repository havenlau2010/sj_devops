const { spawn } = require('child_process');
const path = require('path');

// 定义需要编译的项目列表
const projects = [
  {
    name: 'efacms_infrastructure',
    path: path.resolve(__dirname, '../0000急诊急救临床管理系统/front-end/efacms_infrastructure'), // 项目绝对路径
    buildCmd: 'npm run build' // 编译命令
  },
  {
    name: 'pre_examination_triage_upgrade',
    path: path.resolve(__dirname, '../0002预检分诊管理系统/front-end/pre_examination_triage_upgrade'),
    buildCmd: 'npm run build'
  },
  {
    name: 'ecic_critical_illness_center',
    path: path.resolve(__dirname, '../0003专病中心管理系统/front-end/ecic_critical_illness_center'),
    buildCmd: 'npm run build'
  }
];

// 并行启动编译进程
function buildAllProjects() {
  projects.forEach(project => {
    console.log(`[${project.name}] 开始编译...`);
    // 拆分命令（npm run build → ['npm', 'run', 'build']）
    let [cmd, ...args] = project.buildCmd.split(' ');
    
    // Windows 下 npm 实际上是 npm.cmd
    if (process.platform === 'win32' && cmd === 'npm') {
      cmd = 'npm.cmd';
    }

    // 启动子进程
    const buildProcess = spawn(cmd, args, {
      cwd: project.path, // 切换到项目目录执行
      stdio: 'inherit' // 共享控制台输出（日志直接打印）
    });

    // 监听编译完成/失败
    buildProcess.on('close', (code) => {
      if (code === 0) {
        console.log(`[${project.name}] 编译成功 ✅`);
      } else {
        console.error(`[${project.name}] 编译失败 ❌ 退出码：${code}`);
      }
      console.log(`time-finish:=>:` + new Date().toLocaleString());
    });
    
  });
}
console.log(`time-start:=>:` + new Date().toLocaleString());
// 执行并行编译
buildAllProjects();
