(function () {
	class sketch {
		#canvas;

		get canvas() { return this.#canvas; }
		get context() { return this.#canvas.ctx; }
		get element() { return this.#canvas.element; }

		get width() { return this.canvas.width; }
		get height() { return this.canvas.height; }
		_resize() { }

		constructor(canvas) {
			this.#canvas = canvas;
		}

		_draw(ctx) { }

		_pointerEnter(x, y) { }
		_pointerLeave(x, y) { }

		_pointerMove(x, y, cap) { }

		_pointerDown(x, y) { }
		_pointerUp(x, y) { }

		_pointerCapture(x, y, cap) { }
		_pointerRelease(x, y, cap) { }
	}

	class webglSketch extends sketch {
		static get contextType() { return "webgl2"; }

		constructor(canvas) {
			super(canvas);
		}

		_clearColor = [0, 0, 0, 1];

		_draw(gl) {
			gl.clearDepth(1);

			var cc = this._clearColor;
			gl.clearColor(cc[0], cc[1], cc[2], cc[3]);

			gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);

			gl.viewport(0, 0, this.width, this.height);
		}

		compileVertexShader(code) { return this.compileShader(this.context.VERTEX_SHADER, code); }
		compileFragmentShader(code) { return this.compileShader(this.context.FRAGMENT_SHADER, code); }

		compileShader(type, code) {
			const gl = this.context;

			const ret = gl.createShader(type);
			gl.shaderSource(ret, code);
			gl.compileShader(ret);

			if (!gl.getShaderParameter(ret, gl.COMPILE_STATUS)) {
				const message = gl.getShaderInfoLog(ret);
				gl.deleteShader(ret);
				throw new Error('shader compilation failed\n' + message);
			}

			return ret;
		}

		linkProgram(shaders) {
			const gl = this.context;

			const ret = gl.createProgram();

			for (var sh of shaders)
				gl.attachShader(ret, sh);

			gl.linkProgram(ret);

			if (!gl.getProgramParameter(ret, gl.LINK_STATUS)) {
				const message = gl.getProgramInfoLog(ret);
				gl.deleteProgram(ret);
				throw new Error('program link failed\n' + message);
			}

			class uniform {
				#gl;
				#info;
				#location;

				constructor(gl, prog, index) {
					this.#gl = gl;
					this.#info = gl.getActiveUniform(prog, index);
					this.#location = gl.getUniformLocation(prog, this.#info.name);
				}

				get name() { return this.#info.name; }
				get type() { return this.#info.type; }
				get location() { return this.#location; }

				set(value) {
					const gl = this.#gl;

					const t = this.type;
					if (t == gl.FLOAT_MAT4) {
						if (value.length == 12) {
							const newValue = new Float32Array(16);
							newValue[0] = value[0];
							newValue[1] = value[1];
							newValue[2] = value[2];
							newValue[3] = 0;
							newValue[4] = value[3];
							newValue[5] = value[4];
							newValue[6] = value[5];
							newValue[7] = 0;
							newValue[8] = value[6];
							newValue[9] = value[7];
							newValue[10] = value[8];
							newValue[11] = 0;
							newValue[12] = value[9];
							newValue[13] = value[10];
							newValue[14] = value[11];
							newValue[15] = 1;
							value = newValue;
						}

						if (value.length != 16)
							throw new Error('mat4 values must be float[16]');

						gl.uniformMatrix4fv(this.#location, false, value);
					} else if (t == gl.FLOAT_VEC2) {
						gl.uniform2fv(this.#location, value);
					} else {
						throw new Error('Unsupported parameter type.');
					}
				}
			}

			ret.uniforms = {};
			const numUniforms = gl.getProgramParameter(ret, gl.ACTIVE_UNIFORMS);
			ret.uniforms.length = numUniforms;
			for (var i = 0; i < numUniforms; i++) {
				const u = new uniform(gl, ret, i);
				ret.uniforms[u.name] = u;
				ret.uniforms[i] = u;
			}

			return ret;
		}

		compileProgram(vertexShaderCode, fragmentShaderCode) {
			var vs = this.compileVertexShader(vertexShaderCode);
			try {
				var fs = this.compileFragmentShader(fragmentShaderCode);
				try {
					return this.linkProgram([vs, fs]);
				} catch (e) {
					this.context.deleteShader(fs);
					throw e;
				}
			} catch (e) {
				this.context.deleteShader(vs);
				throw e;
			}
		}
	}

	//#region math

	class vec3 extends Float32Array {
		constructor(x = 0, y = 0, z = 0) {
			super(3);
			this[0] = x;
			this[1] = y;
			this[2] = z;
		}

		get x() { return this[0]; }
		set x(v) { this[0] = v; }
		get y() { return this[1]; }
		set y(v) { this[1] = v; }
		get z() { return this[2]; }
		set z(v) { this[2] = v; }

		clone() {
			return new vec3(this[0], this[1], this[2]);
		}
		static clone(v) {
			return new vec3(v[0], v[1], v[2]);
		}

		lengthSquared() {
			return vec3.lengthSquared(this);
		}
		static lengthSquared(v) {
			const x = v[0];
			const y = v[1];
			const z = v[2];
			return x * x + y * y + z * z;
		}

		length() {
			return vec3.length(this);
		}
		static length(v) {
			return Math.sqrt(vec3.lengthSquared(v));
		}

		scaleBy(s) {
			this[0] *= s;
			this[1] *= s;
			this[2] *= s;
			return this;
		}

		normalize() {
			return this.scaleBy(1 / this.length());
		}

		add(a) {
			this[0] += a[0];
			this[1] += a[1];
			this[2] += a[2];
			return this;
		}

		setAdd(a, b) {
			this[0] = a[0] + b[0];
			this[1] = a[1] + b[1];
			this[2] = a[2] + b[2];
			return this;
		}

		static add(a, b) {
			return new vec3(
				a[0] + b[0],
				a[1] + b[1],
				a[2] + b[2]);
		}

		sub(a) {
			this[0] -= a[0];
			this[1] -= a[1];
			this[2] -= a[2];
			return this;
		}

		setSub(a, b) {
			this[0] = a[0] - b[0];
			this[1] = a[1] - b[1];
			this[2] = a[2] - b[2];
			return this;
		}

		static sub(a, b) {
			return new vec3(
				a[0] - b[0],
				a[1] - b[1],
				a[2] - b[2]);
		}

		dot(a) {
			return this[0] * a[0] + this[1] * a[1] + this[2] * a[2];
		}

		static dot(a, b) {
			return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
		}

		static cross(a, b) {
			return new vec3(
				a[1] * b[2] - a[2] * b[1],
				a[2] * b[0] - a[0] * b[2],
				a[0] * b[1] - a[1] * b[0]);
		}
	}

	class quat extends Float32Array {
		constructor(i = 0, j = 0, k = 0, w = 1) {
			super(4);
			this[0] = i;
			this[1] = j;
			this[2] = k;
			this[3] = w;
		}

		get i() { return this[0]; }
		set i(v) { this[0] = v; }
		get j() { return this[1]; }
		set j(v) { this[1] = v; }
		get k() { return this[2]; }
		set k(v) { this[2] = v; }
		get w() { return this[3]; }
		set w(v) { this[3] = v; }

		clone() {
			return quat.clone(this);
		}
		static clone(q) {
			var ret = new quat();
			ret[0] = q[0];
			ret[1] = q[1];
			ret[2] = q[2];
			ret[3] = q[3];
			return ret;
		}

		static identity() {
			var ret = new quat();
			ret[3] = 1;
			return ret;
		}

		static axisAngle(axis, angle) {
			angle *= 0.5;
			var s = Math.sin(angle);
			var c = Math.cos(angle);

			s /= vec3.length(axis);

			var ret = new quat();
			ret[0] = axis[0] * s;
			ret[1] = axis[1] * s;
			ret[2] = axis[2] * s;
			ret[3] = c;
			return ret;
		}

		setMul(a, b) {
			const ai = a[0];
			const aj = a[1];
			const ak = a[2];
			const aw = a[3];

			const bi = b[0];
			const bj = b[1];
			const bk = b[2];
			const bw = b[3];

			this[0] = ai * bw + aw * bi + aj * bk - ak * bj;
			this[1] = aj * bw + aw * bj + ak * bi - ai * bk;
			this[2] = ak * bw + aw * bk + ai * bj - aj * bi;
			this[3] = aw * bw - ai * bi - aj * bj - ak * bk;

			return this;
		}
		mul(a) {
			return this.setMul(this, a);
		}
		static mul(a, b) {
			return new quat().setMul(a, b);
		}

		rotatePoint(p)
		{
			const i = this[0];
			const j = this[1];
			const k = this[2];
			const w = this[3];

			const i2 = i + i;
			const j2 = j + j;
			const k2 = k + k;

			const wi2 = w * i2;
			const wj2 = w * j2;
			const wk2 = w * k2;
			const ii2 = i * i2;
			const ij2 = i * j2;
			const ik2 = i * k2;
			const jj2 = j * j2;
			const jk2 = j * k2;
			const kk2 = k * k2;

			return new vec3(
				p[0] * (1 - jj2 - kk2) + p[1] * (ij2 - wk2) + p[2] * (ik2 + wj2),
				p[0] * (ij2 + wk2) + p[1] * (1 - ii2 - kk2) + p[2] * (jk2 - wi2),
				p[0] * (ik2 - wj2) + p[1] * (jk2 + wi2) + p[2] * (1 - ii2 - jj2));
		}
	}

	class ma4x4 extends Float32Array {
		constructor() {
			super(12);
		}

		static identity() {
			var ret = new ma4x4();
			ret[0] = 1;
			ret[4] = 1;
			ret[8] = 1;
			return ret;
		}

		setViewLookAt(pos, target, up) {
			const vf = vec3.sub(pos, target).normalize();
			const vs = vec3.cross(up, vf).normalize();
			const vu = vec3.cross(vf, vs);

			this[0] = vs[0];
			this[1] = vu[0];
			this[2] = vf[0];

			this[3] = vs[1];
			this[4] = vu[1];
			this[5] = vf[1];

			this[6] = vs[2];
			this[7] = vu[2];
			this[8] = vf[2];

			this[9] = -vec3.dot(pos, vs);
			this[10] = -vec3.dot(pos, vu);
			this[11] = -vec3.dot(pos, vf);

			return this;
		}

		static viewLookAt(pos, target, up) {
			return new ma4x4().setViewLookAt(pos, target, up);
		}

		static inverse(a) {
			//compute the 2x2 minors for C03
			const m0_0 = a[4] * a[8] - a[5] * a[7];
			const m0_1 = a[5] * a[6] - a[3] * a[8];
			const m0_2 = a[3] * a[7] - a[4] * a[6];

			//compute the 2x2 minors for C13
			const m1_0 = a[2] * a[7] - a[1] * a[8];
			const m1_1 = a[0] * a[8] - a[2] * a[6];
			const m1_2 = a[1] * a[6] - a[0] * a[7];

			//compute the 2x2 minors for C23 and det(a)
			const m2_0 = a[1] * a[5] - a[2] * a[4];
			const m2_1 = a[2] * a[3] - a[0] * a[5];
			const m2_2 = a[0] * a[4] - a[1] * a[3];

			//compute the determinant and its inverse
			const in_det =
				a[6] * m2_0 +
				a[7] * m2_1 +
				a[8] * m2_2;

			//more of a sanity check than anything - this shouln't really happen
			if (Math.abs(in_det) < 1e-7)
			{
				out_det = 0;
				return a;
			}

			const inv_det = 1.0 / in_det;

			//compute the adjoint's fourth column
			const a3_0 = a[9] * m0_0 + a[10] * m0_1 + a[11] * m0_2;
			const a3_1 = a[9] * m1_0 + a[10] * m1_1 + a[11] * m1_2;
			const a3_2 = a[9] * m2_0 + a[10] * m2_1 + a[11] * m2_2;

			//compute the inverse
			var ret = new ma4x4();

			ret[0] = inv_det * m0_0;
			ret[1] = inv_det * m1_0;
			ret[2] = inv_det * m2_0;

			ret[3] = inv_det * m0_1;
			ret[4] = inv_det * m1_1;
			ret[5] = inv_det * m2_1;

			ret[6] = inv_det * m0_2;
			ret[7] = inv_det * m1_2;
			ret[8] = inv_det * m2_2;

			ret[9] = -inv_det * a3_0;
			ret[10] = -inv_det * a3_1;
			ret[11] = -inv_det * a3_2;

			return ret;
		}
	}

	class m4x4 extends Float32Array {
		constructor() {
			super(16);
		}

		static identity() {
			var ret = new m4x4();
			ret[0] = 1;
			ret[5] = 1;
			ret[10] = 1;
			ret[15] = 1;
			return ret;
		}

		setProjectPerspectiveFov(fovY, aspectWOverH, zNear, zFar = undefined) {
			const sy = 1.0 / Math.tan(fovY * 0.5);
			const sx = sy / aspectWOverH;

			this[0] = sx;
			this[1] = 0;
			this[2] = 0;
			this[3] = 0;

			this[4] = 0;
			this[5] = sy;
			this[6] = 0;
			this[7] = 0;

			this[8] = 0;
			this[9] = 0;
			this[11] = -1;

			this[12] = 0;
			this[13] = 0;
			this[15] = 0;

			if (zFar !== undefined) {
				const invZd = 1 / (zFar - zNear);
				this[10] = -(zNear + zFar) * invZd;
				this[14] = -2 * zNear * zFar * invZd;
			} else {
				this[10] = -1;
				this[14] = -2 * zNear;
			}

			return this;
		}

		static projectPerspectiveFov(fovY, aspectWOverH, zNear, zFar = undefined) {
			return new m4x4().setProjectPerspectiveFov(fovY, aspectWOverH, zNear, zFar);
		}
	}

	//#endregion

	class canvas {
		element;
		ctx;
		sketch;

		constructor(sketchElement, sketch) {
			const canvasElement = document.createElement('canvas');

			this.element = canvasElement;

			function renderFallback(errorClass) {
				sketchElement.classList.add('sketch-fallback');
				if (errorClass)
					sketchElement.classList.add(errorClass);

				var fallbackContent;

				const fallbackImage = sketch.fallbackImage;
				if (fallbackImage) {
					fallbackContent = document.createElement('img');
					fallbackContent.setAttribute('src', fallbackImage);
				} else {
					fallbackContent = document.createElement('p');
					fallbackContent.innerText = 'This interactive diagram failed to load.';
				}

				fallbackContent.classList.add('fallback-content');
				sketchElement.appendChild(fallbackContent);
			}

			canvasElement.classList.add(`canvas-api-${sketch.contextType}`)
			this.ctx = canvasElement.getContext(sketch.contextType);
			if (!this.ctx) {
				renderFallback('canvas-api-not-supported');
				return;
			}

			try {
				this.sketch = new sketch(this);
			} catch (e) {
				renderFallback('sketch-initialization-failed');
				return;
			}

			//wire it up

			sketchElement.appendChild(canvasElement);
			sketchElement.sketchCanvas = canvasElement.sketchCanvas = this;

			canvas.#resizeObserver ??= new ResizeObserver(canvas.#onResized);
			canvas.#resizeObserver.observe(sketchElement);

			//input

			const self = this;

			var pointerId = null;
			var pointerIsDown = false;
			var currentCapture = null;

			var px, py;

			function pointerMove(e) {
				if (pointerId == null)
				{
					px = null;
					py = null;

					const o = self.#pointerEnter(e.offsetX, e.offsetY);
					if (o?.redraw)
						self.#invalidate();
					pointerId = e.pointerId;
				}
				else if (e.pointerId != pointerId)
					return;

				if (e.offsetX != px || e.offsetY != py)
				{
					px = e.offsetX;
					py = e.offsetY;

					const o = self.#pointerMove(px, py, currentCapture);
					if (o?.redraw)
						self.#invalidate();
				}
			}

			function pointerDown(e) {
				if (e.pointerId != pointerId)
					return;

				if (pointerIsDown) {
					console.error('mixed up pointerIsDown value');
					return;
				}

				pointerIsDown = true;

				const o = self.#pointerDown(e.offsetX, e.offsetY);
				if (o?.redraw)
					self.#invalidate();

				if (o?.capture) {
					console.assert(!currentCapture);

					currentCapture = o.capture;
					const o2 = self.#pointerCapture(currentCapture);
					if (o2?.redraw)
						self.#invalidate();

					canvasElement.setPointerCapture(e.pointerId)
				}

				if (currentCapture) {
					e.preventDefault();
					e.stopPropagation();
				}
			}

			function pointerUp(e) {
				if (e.pointerId != pointerId)
					return;

				if (!pointerIsDown)
					return;

				const o = self.#pointerUp(e.offsetX, e.offsetY, currentCapture);
				if (o?.redraw)
					self.#invalidate();

				if (currentCapture) {
					const o2 = self.#pointerRelease(e.offsetX, e.offsetY, currentCapture);
					if (o2?.redraw)
						self.#invalidate();
					
					canvasElement.releasePointerCapture(e.pointerId);
					currentCapture = null;

					e.preventDefault();
					e.stopPropagation();
				}

				pointerIsDown = false;
			}

			function pointerLeave(e) {
				if (e.pointerId != pointerId)
					return;

				pointerUp(e);
				console.assert(!pointerIsDown);

				const o = self.#pointerLeave(e.offsetX, e.offsetY);
				if (o?.redraw)
					self.#invalidate();

				pointerId = null;
				px = py = null;
			}

			const eventOptions = { capture: true, passive: false };
			canvasElement.addEventListener('pointermove',
				function (e) {
					pointerMove(e);
				}, eventOptions)
			canvasElement.addEventListener('touchmove',
				function (e) {
					//this is incredibly dumb, but calling preventDefault
					//on *pointer* events doesn't work because ...reasons
					if (currentCapture) {
						e.preventDefault()
						e.stopPropagation()
					}
				}, eventOptions)
			canvasElement.addEventListener('pointerdown',
				function (e) {
					pointerMove(e);
					pointerDown(e);
				}, eventOptions)
			canvasElement.addEventListener('pointerup',
				function (e) {
					pointerMove(e);
					pointerUp(e);
				}, eventOptions)
			canvasElement.addEventListener('pointerleave',
				function (e) {
					pointerLeave(e);
				}, eventOptions)
			canvasElement.addEventListener('pointercancel',
				function (e) {
					pointerLeave(e);
				}, eventOptions)
		}

		#pointerCapture(x, y, cap) {
			x = this.#ptolX(x);
			y = this.#ptolY(y);

			return this.sketch._pointerCapture(x, y, cap);
		}
		#pointerRelease(x, y, oldCap) {
			x = this.#ptolX(x);
			y = this.#ptolY(y);

			return this.sketch._pointerRelease(x, y, oldCap);
		}

		#pointerEnter(x, y) {
			x = this.#ptolX(x);
			y = this.#ptolY(y);

			return this.sketch._pointerEnter(x, y);
		}
		#pointerDown(x, y) {
			x = this.#ptolX(x);
			y = this.#ptolY(y);

			return this.sketch._pointerDown(x, y);
		}
		#pointerUp(x, y, cap) {
			x = this.#ptolX(x);
			y = this.#ptolY(y);

			return this.sketch._pointerUp(x, y);
		}
		#pointerMove(x, y, cap) {
			x = this.#ptolX(x);
			y = this.#ptolY(y);

			return this.sketch._pointerMove(x, y, cap);
		}
		#pointerLeave(x, y) {
			x = this.#ptolX(x);
			y = this.#ptolY(y);

			return this.sketch._pointerLeave(x, y);
		}

		#invalidate() { this.#draw(); } // :(
		#draw() {
			this.sketch._draw(this.ctx);
		}

		get width() { return this.element.width; }
		get height() { return this.element.height; }

		#ptolX(x) {
			var w = this.element.clientWidth;
			var h = this.element.clientHeight;

			var s = Math.min(w, h);

			var o = (w - s) / 2;
			x = (x - o) / s;
			x = x * 2 - 1;

			return x;
		}

		#ptolY(y) {
			var w = this.element.clientWidth;
			var h = this.element.clientHeight;

			var s = Math.min(w, h);

			var o = (h - s) / 2;
			y = (y - o) / s;
			y = y * 2 - 1;

			return -y;
		}

		#resize() {
			var scale = window.devicePixelRatio;

			var e = this.element;
			e.width = Math.floor(e.clientWidth * scale);
			e.height = Math.floor(e.clientHeight * scale);

			this.sketch._resize();

			this.#draw();
		}

		static #resizeObserver;
		static #onResized(items) {
			for (var it of items)
				it.target.sketchCanvas.#resize();
		}
	}

	window.sketch3d = {
		webglSketch,

		load: function (sketch) {
			var me = document.currentScript;

			const containerElem = document.createElement('div');
			containerElem.classList.add('sketch');

			new canvas(containerElem, sketch);

			var caption = sketch.captionHTML;
			if (caption) {
				const captionElem = document.createElement('div');
				captionElem.classList.add('caption');
				captionElem.innerHTML = caption;
				containerElem.appendChild(captionElem);
			}

			me.insertAdjacentElement('afterend', containerElem);
		},

		math: {
			vec3,
			quat,
			ma4x4,
			m4x4,
		},
	};
})();