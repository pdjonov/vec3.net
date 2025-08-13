(function () { 
	function clamp(min, v, max) {
		if (v < min) v = min
		else if (v > max) v = max
		return v
	}
	function clampToLogCoords(v) {
		return clamp(-1, v, 1)
	}

	class drawable
	{
		constructor(props) {
			this.alpha = 1

			if (props)
				for (var k in props)
					if (this[k] === undefined)
						this[k] = props[k]
		}

		draw(ctx) {
		}

		ltop(x, y) { return this.canvas.ltop(x, y) }
		ltopScale() { return this.canvas.ltopScale() }

		//interaction:

		//return true if this element contains the pointer cursor
		hitTest(x, y) { return false }

		//called once when hitTest starts to return true
		pointerEnter(x, y) { }

		//only called if hitTest returned true
		//return true to capture the pointer and get move events
		pointerDown(x, y) { return false }

		//while the pointer is entered (or captured), move events are routed here
		pointerMove(x, y, captured) { }

		//when the pointer is released (and capture is released) this is called
		pointerUp(x, y) { }

		//called once when capture is released and hitTest returns false
		pointerLeave(x, y) { }
	}

	class grid extends drawable
	{
		constructor(props) {
			super(props);

			this.stroke ??= '#444'
			this.axesStroke ??= '#555'
		}

		draw(ctx) {
			ctx.strokeStyle = this.stroke
			ctx.lineWidth = 1 / this.canvas.renderScale()

			var {x: x0, y: y0} = this.ltop(-1, -1);
			var {x: x1, y: y1} = this.ltop(1, 1);

			//y-coords are flipped in physical coords
			//so we need to correct that so the loops make sense
			([y0, y1] = [y1, y0])

			var w = x1 - x0
			var h = y1 - y0

			var s = Math.min(w, h) / 32

			ctx.beginPath()

			for (var x = x0; x <= x1; x += s)
			{
				ctx.moveTo(x, y0)
				ctx.lineTo(x, y1)
			}

			for (var y = y0; y <= y1; y += s)
			{
				ctx.moveTo(x0, y)
				ctx.lineTo(x1, y)
			}

			ctx.stroke()

			var {x: cx, y: cy} = this.ltop(0, 0)

			ctx.strokeStyle = this.axesStroke
			ctx.lineWidth = 3 / this.canvas.renderScale()

			ctx.beginPath()

			ctx.moveTo(cx, y0)
			ctx.lineTo(cx, y1)

			ctx.moveTo(x0, cy)
			ctx.lineTo(x1, cy)

			ctx.stroke()
		}
	}

	class circle extends drawable {
		constructor(x, y, r, props) {
			super(props)

			this.x = clampToLogCoords(x)
			this.y = clampToLogCoords(y)
			if (r !== undefined)
				this.r = clamp(0.1, r, 1)

			this.movable ??= true
			this.resizable ??= true
		}

		contains(p) {
			var dx = this.x - p.x
			var dy = this.y - p.y

			var r = this.r

			return dx * dx + dy * dy <= r * r
		}

		draw(ctx) {
			let { x, y } = this.ltop(this.x, this.y)
			let r = this.ltopScale() * this.r

			ctx.lineWidth = 3
			ctx.strokeStyle = this.stroke
			ctx.fillStyle = this.fill

			ctx.beginPath()

			ctx.arc(x, y, r, 0, 2 * Math.PI)

			ctx.closePath()

			if (this.stroke) {
				var fade = this.interactive && this.hover !== 'resize'
				if (fade) {
					ctx.save()
					ctx.globalAlpha *= 0.75
				}
				ctx.stroke()
				if (fade)
					ctx.restore()
			}
			if (this.fill) {
				var fade = this.interactive && this.hover !== 'move'
				if (fade) {
					ctx.save()
					ctx.globalAlpha *= 0.75
				}
				ctx.fill()
				if (fade)
					ctx.restore()
			}
		}

		hitTest(x, y) {
			var dx = x - this.x
			var dy = y - this.y

			var r = this.r + 2 / this.ltopScale()

			return dx * dx + dy * dy < r * r
		}

		pointerDown(x, y) {
			this.interact = this.hover
			this.interactDx = this.x - x
			this.interactDy = this.y - y
			return true
		}

		pointerMove(x, y, captured) {
			var oldHover = this.hover
			var oldX = this.x
			var oldY = this.y
			var oldR = this.r

			if (!captured) {
				var dx = x - this.x
				var dy = y - this.y

				var ri = Math.max(this.r - 2 / this.ltopScale(), 0)
				//var ro = this.r + 2 / this.ltopScale()

				var d2 = dx * dx + dy * dy

				this.hover = null
				if (d2 > ri * ri) {
					if (this.resizable)
						this.hover = 'resize'
					else if (this.movable)
						this.hover = 'move'
				} else {
					if (this.movable)
						this.hover = 'move'
				}
			} else if (this.interact == 'resize') {
				var dx = x - this.x
				var dy = y - this.y

				var r = Math.sqrt(dx * dx + dy * dy)
				this.r = clamp(0.1, r, 1)
			} else if (this.interact == 'move') {
				this.x = clampToLogCoords(this.interactDx + x)
				this.y = clampToLogCoords(this.interactDy + y)
			}

			return oldHover != this.hover ||
				oldX != this.x || oldY != this.y ||
				oldR != this.r
		}

		pointerUp(x, y) {
			this.interact = null
		}

		pointerLeave(x, y) {
			this.hover = null
		}
	}

	class point extends circle {
		constructor(x, y, props) {
			super(x, y, undefined, props)

			this.r ??= 0.04
			this.fill ??= '#888'
			this.resizable = false
		}
	}

	class line extends drawable {
		constructor(a, b, props) {
			super(props)

			this.a = a
			this.b = b

			this.stroke ??= '#888'
			this.lineWidth ??= 2

			this.toInfinity ??= false
			this.extendPastA ??= 0
			this.extendPastB ??= 0
		}

		//returns a unit vector pointing from a towards b
		//if scale is provided then the vector is scaled by that value
		direction(scale) {
			var dx = this.b.x - this.a.x
			var dy = this.b.y - this.a.y

			var s = (scale ?? 1) / Math.sqrt(dx * dx + dy * dy)

			return {x: dx * s, y: dy * s}
		}

		draw(ctx) {
			var ax = this.a.x
			var ay = this.a.y

			var bx = this.b.x
			var by = this.b.y

			if (this.toInfinity) {
				var vx = bx - ax
				var vy = by - ay

				//so... unfathomably... lazy...
				let c = 10000

				ax -= vx * c
				ay -= vy * c

				bx += vx * c
				by += vy * c
			} else if (this.extendPastA > 0 || this.extendPastB > 0) {
				var vx = bx - ax
				var vy = by - ay

				var s = 1 / Math.sqrt(vx * vx + vy * vy)

				ax -= vx * this.extendPastA * s
				ay -= vy * this.extendPastA * s

				bx += vx * this.extendPastB * s
				by += vy * this.extendPastB * s
			}

			({x: ax, y: ay} = this.ltop(ax, ay));
			({x: bx, y: by} = this.ltop(bx, by));

			ctx.strokeStyle = this.stroke
			ctx.lineWidth = this.lineWidth
			ctx.lineCap = 'round'

			ctx.beginPath()

			ctx.moveTo(ax, ay)
			ctx.lineTo(bx, by)

			ctx.stroke()
		}
	}

	class halfspace extends drawable {
		constructor(a, b, props) {
			super(props)

			this.a = a
			this.b = b

			this.stroke ??= '#888'
			this.fill ??= '#888'
			this.lineWidth ??= 1

			this.side ??= 1
			this.slice ??= 0
		}

		//returns true if p is on the shaded side of the plane
		contains(p) {
			var vx = this.b.x - this.a.x
			var vy = this.b.y - this.a.y

			var px = p.x - this.a.x
			var py = p.y - this.a.y

			return (vx * px + vy * py) * this.side <= 0
		}

		draw(ctx) {
			var ax = this.a.x
			var ay = this.a.y

			var bx = this.b.x
			var by = this.b.y

			var vx = bx - ax
			var vy = by - ay;

			var gx, gy
			if (this.fill) {
				var vl = Math.sqrt(vx * vx + vy * vy)

				var s = 0.3 * this.ltopScale() * this.side / vl

				gx = vx * s
				gy = -vy * s
			}

			[vx, vy] = [-vy, vx]

			//so... unfathomably... lazy...
			let c = 10000

			let x0 = ax
			let y0 = ay
			if (this.slice >= 0) {
				x0 -= vx * c
				y0 -= vy * c
			}

			let x1 = ax
			let y1 = ay
			if (this.slice <= 0) {
				x1 += vx * c
				y1 += vy * c
			}

			({x: x0, y: y0} = this.ltop(x0, y0));
			({x: x1, y: y1} = this.ltop(x1, y1));

			ctx.strokeStyle = this.stroke
			ctx.lineWidth = this.lineWidth
			ctx.lineCap = 'round'

			ctx.beginPath()

			ctx.moveTo(x0, y0)
			ctx.lineTo(x1, y1)

			ctx.stroke()

			if (this.fill) {
				({x: ax, y: ay} = this.ltop(ax, ay))

				var grad = ctx.createLinearGradient(ax, ay, ax - gx, ay - gy)
				grad.addColorStop(0, this.fill)
				grad.addColorStop(1, '#0000')

				ctx.fillStyle = grad

				ctx.lineTo(x1 - gx, y1 - gy)
				ctx.lineTo(x0 - gx, y0 - gy)
				ctx.closePath()

				ctx.fill()
			}
		}
	}

	var resizeObserver = null
	function onResized(items)
	{
		for (var it of items)
			it.target.sketchCanvas.resize()
	}

	class canvas
	{
		constructor(element, sketch) {
			let self = this

			this.element = element

			this.drawables = []
			this.interactives = []

			this.sketch = new sketch(this)
			this.sketch.canvas = this

			if (resizeObserver === null)
				resizeObserver = new ResizeObserver(onResized)

			element.parentElement.sketchCanvas = this
			resizeObserver.observe(element.parentElement)

			function hitTest(x, y) {
				for (var i = self.interactives.length - 1; i >= 0; i--)
					if (self.interactives[i].hitTest?.(x, y))
						return self.interactives[i]

				return null
			}

			var hoverItem = null
			var hoverCapture = false

			var pointerId = null
			function handlePointer(e) {
				if (!e.isPrimary)
					return false;

				if (pointerId == null)
					pointerId = e.pointerId

				return pointerId == e.pointerId
			}
			function releasePointer(e) {
				if (pointerId != e.pointerId)
					return false

				if (hoverItem) {
					var { x, y } = self.ptol(e.offsetX, e.offsetY)

					if (hoverCapture)
						hoverItem.pointerLeave(x, y)

					hoverItem.pointerLeave(x, y)
					hoverItem = null
				}

				if (hoverCapture) {
					element.releasePointerCapture(pointerId)
					hoverCapture = false

					e.preventDefault()
					e.stopPropagation()
				}

				self.draw()

				pointerId = null
				return true
			}

			function pointerMove(e) {
				var { x, y } = self.ptol(e.offsetX, e.offsetY)

				var redraw = false

				if (!hoverCapture) {
					var over = hitTest(x, y)
					if (over != hoverItem) {
						hoverItem?.pointerLeave(x, y)
						hoverItem = over
						over?.pointerEnter(x, y)

						redraw = true
					}
				}

				redraw |= hoverItem?.pointerMove(x, y, hoverCapture)

				if (hoverCapture) {
					e.preventDefault()
					e.stopPropagation()
				}

				if (redraw)
					self.draw()

				return true
			}

			let eventOptions = { capture: true, passive: false };

			element.addEventListener('pointermove',
				function (e) {
					if (!handlePointer(e))
						return

					pointerMove(e)
				}, eventOptions)
			element.addEventListener('touchmove',
				function (e) {
					//this is incredibly dumb, but calling preventDefault
					//on *pointer* events doesn't work because ...reasons
					if (hoverCapture) {
						e.preventDefault()
						e.stopPropagation()
					}
				}, eventOptions)
			element.addEventListener('pointerdown',
				function (e) {
					if (!handlePointer(e))
						return

					if (!pointerMove(e))
						return

					var { x, y } = self.ptol(e.offsetX, e.offsetY)

					if (hoverItem?.pointerDown(x, y)) {
						element.setPointerCapture(e.pointerId)
						hoverCapture = true
					}

					if (hoverCapture) {
						e.preventDefault()
						e.stopPropagation()
					}

					self.draw()
				}, eventOptions)
			element.addEventListener('pointerup',
				function (e) {
					if (!releasePointer(e))
						return

					pointerMove(e)
				}, eventOptions)
			element.addEventListener('pointerleave', releasePointer, eventOptions)
			element.addEventListener('pointercancel', releasePointer, eventOptions)
		}

		renderScale()
		{
			var elem = this.element
			return Math.max(elem.width / elem.clientWidth, elem.height / elem.clientHeight)
		}

		ltopScale()
		{
			var w = this.element.clientWidth
			var h = this.element.clientHeight

			return Math.min(w, h) * 0.5
		}
		ltop(x, y)
		{
			var w = this.element.clientWidth
			var h = this.element.clientHeight

			var s = Math.min(w, h)

			var x0 = (w - s) / 2
			var y0 = (h - s) / 2

			x = (x + 1) / 2
			y = (-y + 1) / 2

			return {x: x0 + x * s, y: y0 + y * s}
		}

		ptolScale() { return 1 / this.ltopScale() }
		ptol(x, y)
		{
			var w = this.element.clientWidth
			var h = this.element.clientHeight

			var s = Math.min(w, h)

			var x0 = (w - s) / 2
			var y0 = (h - s) / 2

			x = (x - x0) / s
			y = (y - y0) / s

			x = x * 2 - 1
			y = y * 2 - 1

			return {x: x, y: -y}
		}

		add(drawable, interactive)
		{
			drawable.canvas = this
			drawable.interactive = interactive
			this.drawables.push(drawable)

			if (interactive)
				this.interactives.push(drawable)
		}

		resize()
		{
			var scale = window.devicePixelRatio

			var elem = this.element
			elem.width = Math.floor(elem.clientWidth * scale)
			elem.height = Math.floor(elem.clientHeight * scale)

			this.draw()
		}

		draw()
		{
			this.sketch.update?.()
			for (var d of this.drawables)
				d.update?.()

			var ctx = this.element.getContext('2d')

			ctx.reset()

			var elem = this.element
			ctx.clearRect(0, 0, elem.width, elem.height)

			ctx.scale(elem.width / elem.clientWidth, elem.height / elem.clientHeight)

			for (var d of this.drawables)
			{
				ctx.save()
				ctx.globalAlpha = d.alpha

				d.draw(ctx)

				ctx.restore()

				if (d.label && typeof (d.x) === 'number' && typeof (d.y) === 'number') {
					var {x, y} = this.ltop(d.x, d.y)

					ctx.save()
					ctx.font = "bold 2em 'Trebuchet MS', Tahoma, Arial, sans-serif"
					ctx.fillStyle = '#CCC'
					ctx.fillText(d.label, x, y)
					ctx.restore()
				}
			}
		}
	}

	window.sketch = {
		grid,

		circle,
		point,

		line,
		halfspace,

		load: function (sketch) {
			var scripts = document.getElementsByTagName('script')
			var me = scripts[scripts.length - 1]

			var containerElem = document.createElement('div')
			containerElem.classList.add('sketch')

			var canvasElem = document.createElement('canvas')
			containerElem.appendChild(canvasElem)

			containerElem.sketchCanvas = canvasElem.sketchCanvas = new canvas(canvasElem, sketch);
	
			me.insertAdjacentElement('afterend', containerElem);
		},

		math: {
			//euclidean distance between point(-like object)s a and b
			distance: function(a, b) {
				var dx = a.x - b.x
				var dy = a.y - b.y
				return Math.sqrt(dx * dx + dy * dy)
			},

			dot: function(a, b) {
				return a.x * b.x + a.y * b.y
			},

			//+1 or -1 to indicate the winding order of triangle abc, or 0 if it's degenerate
			winding: function(a, b, c) {
				return Math.sign((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x))
			}
		}
	};
})();