import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TestApi } from './test-api';

describe('TestApi', () => {
  let component: TestApi;
  let fixture: ComponentFixture<TestApi>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestApi]
    })
    .compileComponents();

    fixture = TestBed.createComponent(TestApi);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
