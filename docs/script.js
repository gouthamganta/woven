// Particle background
const canvas = document.getElementById('bg-canvas');
const ctx = canvas.getContext('2d');
canvas.width = window.innerWidth;
canvas.height = window.innerHeight;

const particles = [];
for (let i = 0; i < 80; i++) {
  particles.push({
    x: Math.random() * canvas.width,
    y: Math.random() * canvas.height,
    vx: (Math.random() - 0.5) * 0.3,
    vy: (Math.random() - 0.5) * 0.3,
    size: Math.random() * 2 + 1
  });
}

function animate() {
  ctx.fillStyle = 'rgba(10, 5, 20, 0.05)';
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  particles.forEach(p => {
    p.x += p.vx;
    p.y += p.vy;
    if (p.x < 0 || p.x > canvas.width) p.vx *= -1;
    if (p.y < 0 || p.y > canvas.height) p.vy *= -1;
    ctx.fillStyle = 'rgba(196, 164, 255, 0.6)';
    ctx.beginPath();
    ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2);
    ctx.fill();
  });
  requestAnimationFrame(animate);
}
animate();

window.addEventListener('resize', () => {
  canvas.width = window.innerWidth;
  canvas.height = window.innerHeight;
});

// Form handling
document.getElementById('registrationForm').addEventListener('submit', function(e) {
  e.preventDefault();
  const form = this;
  const formData = new FormData(form);
  console.log('Registration:', Object.fromEntries(formData));
  form.style.display = 'none';
  document.getElementById('successMessage').style.display = 'block';
});
