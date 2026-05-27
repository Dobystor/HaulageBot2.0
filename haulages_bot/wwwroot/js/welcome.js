const canvas = document.getElementById('backgroundCanvas');
const ctx = canvas.getContext('2d');

// Configuración del canvas
canvas.width = window.innerWidth;
canvas.height = window.innerHeight;

const particlesArray = [];
const numberOfParticles = 50; // Reducir la cantidad de nodos

// Crear el objeto Partícula
class Particle {
    constructor(x, y, size, color, speedX, speedY) {
        this.x = x;
        this.y = y;
        this.size = size;
        this.color = color;
        this.speedX = speedX;
        this.speedY = speedY;
    }

    // Dibujar la partícula
    draw() {
        ctx.beginPath();
        ctx.arc(this.x, this.y, this.size, 0, Math.PI * 2);
        ctx.fillStyle = this.color;
        ctx.fill();
    }

    // Actualizar la posición de la partícula
    update() {
        this.x += this.speedX;
        this.y += this.speedY;

        // Rebote en los bordes
        if (this.x < 0 || this.x > canvas.width) this.speedX *= -1;
        if (this.y < 0 || this.y > canvas.height) this.speedY *= -1;
    }
}

// Crear partículas iniciales
function createParticles() {
    for (let i = 0; i < numberOfParticles; i++) {
        const size = Math.random() * 3 + 1; // Tamaño de las partículas
        const x = Math.random() * canvas.width;
        const y = Math.random() * canvas.height;
        const speedX = Math.random() * 1 - 0.5;
        const speedY = Math.random() * 1 - 0.5;
        const colors = ['#ffffff', '#00aaff', '#cccccc'];
        const color = colors[Math.floor(Math.random() * colors.length)];
        particlesArray.push(new Particle(x, y, size, color, speedX, speedY));
    }
}

// Conectar partículas mediante líneas
function connectParticles() {
    for (let a = 0; a < particlesArray.length; a++) {
        for (let b = a; b < particlesArray.length; b++) {
            const dx = particlesArray[a].x - particlesArray[b].x;
            const dy = particlesArray[a].y - particlesArray[b].y;
            const distance = Math.sqrt(dx * dx + dy * dy);

            // Dibujar líneas si la distancia es menor a 100
            if (distance < 100) {
                ctx.beginPath();
                ctx.strokeStyle = 'rgba(255, 255, 255, 0.1)';
                ctx.lineWidth = 1;
                ctx.moveTo(particlesArray[a].x, particlesArray[a].y);
                ctx.lineTo(particlesArray[b].x, particlesArray[b].y);
                ctx.stroke();
                ctx.closePath();
            }
        }
    }
}

// Animar las partículas
function animate() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    particlesArray.forEach(particle => {
        particle.update();
        particle.draw();
    });
    connectParticles();
    requestAnimationFrame(animate);
}

// Redimensionar el canvas dinámicamente
window.addEventListener('resize', () => {
    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight;
});

createParticles();
animate();