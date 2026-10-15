import { ElementRef } from '@angular/core';
import { vi } from 'vitest';
import { WovenBgComponent } from './woven-bg.component';
import { AppComponent } from '../../app.component';

describe('Animation lifecycle and scroll contracts with a controlled frame clock', () => {
  let frame: FrameRequestCallback | undefined;
  let request: ReturnType<typeof vi.fn>;
  let cancel: ReturnType<typeof vi.fn>;
  let bg: WovenBgComponent;
  let context: any;
  beforeEach(() => {
    request = vi.fn((callback: FrameRequestCallback) => { frame = callback; return 17; }); cancel = vi.fn();
    vi.stubGlobal('requestAnimationFrame', request); vi.stubGlobal('cancelAnimationFrame', cancel);
    context = Object.fromEntries(['clearRect', 'save', 'translate', 'rotate', 'fillText', 'restore'].map(name => [name, vi.fn()]));
    bg = new WovenBgComponent('browser' as unknown as object);
    bg.canvasRef = new ElementRef({ width: 0, height: 0, getContext: vi.fn().mockReturnValue(context) } as unknown as HTMLCanvasElement);
    vi.spyOn(Math, 'random').mockReturnValue(0.5);
  });
  afterEach(() => {
    bg.ngOnDestroy(); vi.restoreAllMocks(); vi.unstubAllGlobals(); frame = undefined;
    for (const property of ['--wx', '--wy', '--scroll']) document.documentElement.style.removeProperty(property);
  });
  it('avoids canvas and frame access on the server', () => {
    const server = new WovenBgComponent('server' as unknown as object); server.ngAfterViewInit();
    expect(request).not.toHaveBeenCalled(); server.ngOnDestroy();
  });
  it('sizes the canvas to the viewport and bounds the particle density', () => {
    bg.ngAfterViewInit(); expect(bg.canvasRef.nativeElement.width).toBe(window.innerWidth);
    expect(bg.canvasRef.nativeElement.height).toBe(window.innerHeight);
    expect((bg as any).parts.length).toBe(Math.min(55, Math.floor(window.innerWidth * window.innerHeight / 22000)));
    expect(context.fillText).toHaveBeenCalledTimes((bg as any).parts.length); expect(request).toHaveBeenCalledOnce();
  });
  it('applies pointer offsets to drawing and resizes the backing canvas', () => {
    bg.ngAfterViewInit(); context.translate.mockClear();
    window.dispatchEvent(new MouseEvent('mousemove', { clientX: 0, clientY: 0 })); frame!(0);
    expect(context.translate).toHaveBeenCalled();
    vi.spyOn(window, 'innerWidth', 'get').mockReturnValue(640);
    vi.spyOn(window, 'innerHeight', 'get').mockReturnValue(480);
    window.dispatchEvent(new Event('resize'));
    expect(bg.canvasRef.nativeElement.width).toBe(640); expect(bg.canvasRef.nativeElement.height).toBe(480);
  });
  it('respawns particles below the viewport after they drift off the top', () => {
    bg.ngAfterViewInit(); const particle = (bg as any).parts[0]; particle.y = -21; frame!(0);
    expect(particle.y).toBe(window.innerHeight + 20); expect(particle.color).not.toContain('OP');
  });
  it('cancels the animation and removes pointer and resize listeners on destruction', () => {
    bg.ngAfterViewInit(); bg.ngOnDestroy(); expect(cancel).toHaveBeenCalledWith(17);
    const before = (bg as any).mouseX; window.dispatchEvent(new MouseEvent('mousemove', { clientX: 999 }));
    expect((bg as any).mouseX).toBe(before);
  });
  it('coalesces scroll events into one frame and writes bounded scroll progress', () => {
    const app = new AppComponent(); app.ngOnInit(); app.onScroll(); app.onScroll(); expect(request).toHaveBeenCalledOnce();
    frame!(0); expect(document.documentElement.style.getPropertyValue('--scroll')).toBe('0.0000');
    expect(document.documentElement.style.getPropertyValue('--wx')).toBe('0.00px');
    app.onScroll(); expect(request).toHaveBeenCalledTimes(2); app.ngOnDestroy(); expect(cancel).toHaveBeenCalledWith(17);
  });
  it('clamps progress at one for scroll beyond the document height', () => {
    vi.spyOn(window, 'scrollY', 'get').mockReturnValue(5000);
    const app = new AppComponent(); app.ngOnInit(); frame!(0);
    expect(document.documentElement.style.getPropertyValue('--scroll')).toBe('1.0000');
    expect(document.documentElement.style.getPropertyValue('--wy')).toBe('-35.71px'); app.ngOnDestroy();
  });
  it('clamps progress at zero for negative overscroll', () => {
    vi.spyOn(window, 'scrollY', 'get').mockReturnValue(-100);
    const app = new AppComponent(); app.ngOnInit(); frame!(0);
    expect(document.documentElement.style.getPropertyValue('--scroll')).toBe('0.0000'); app.ngOnDestroy();
  });
});
