﻿using BriefFiniteElementNet.Common;
using BriefFiniteElementNet.ElementHelpers.Bar;
using BriefFiniteElementNet.Elements;
using BriefFiniteElementNet.Integration;
using BriefFiniteElementNet.Loads;
using BriefFiniteElementNet.Mathh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ElementLocalDof = BriefFiniteElementNet.ElementPermuteHelper.ElementLocalDof;

namespace BriefFiniteElementNet.ElementHelpers.BarHelpers
{
    public class EulerBernoulliBeamHelper2Node : BaseBar2NodeHelper
    {

        public static Matrix GetNMatrixAt(double xi, double l, DofConstraint D0, DofConstraint R0, DofConstraint D1, DofConstraint R1, BeamDirection dir)
        {
            SingleVariablePolynomial[] nss, mss;

            EulerBernouly2NodeShapeFunction.GetShapeFunctions(l, D0, R0, D1, R1, dir, out nss, out mss);

            var buf = new Matrix(4, 4);

            for (var i = 0; i < 2; i++)
            {
                if (nss[i] == null) nss[i] = new SingleVariablePolynomial();
                if (mss[i] == null) mss[i] = new SingleVariablePolynomial();
            }

            for (var i = 0; i < 2; i++)
            {
                var ni = nss[i];
                var mi = mss[i];

                for (var ii = 0; ii < 4; ii++)
                {
                    buf[ii, 2 * i + 0] = ni.EvaluateDerivative(xi, ii);
                    buf[ii, 2 * i + 1] = mi.EvaluateDerivative(xi, ii);
                }
            }


            return buf;
        }

        public static Matrix GetNMatrixAt(double xi, double l, BeamDirection dir)
        {
            return GetNMatrixAt(xi, l, DofConstraint.Fixed, DofConstraint.Fixed, DofConstraint.Fixed, DofConstraint.Fixed, dir);
        }


        public BeamDirection Direction;

        public EulerBernoulliBeamHelper2Node(BeamDirection direction, Element targetElement)
            : base(targetElement)
        {
            Direction = direction;
        }


        #region IElementHelper

        public override Matrix GetBMatrixAt(Element targetElement, params double[] isoCoords)
        {
            var xi = isoCoords[0];
            var bar = targetElement as BarElement;
            var l = (bar.Nodes[1].Location - bar.Nodes[0].Location).Length;

            var buff = GetNMatrixAt(bar, xi);

            //buff is d²N/dξ²
            //but B is d²N/dx²
            //so B will be arr * dξ²/dx² = arr * 1/ j.det ^2
            //based on http://www-g.eng.cam.ac.uk/csml/teaching/4d9/4D9_handout2.pdf

            var j = GetJ(bar);

            var b = buff.ExtractRow(2);

            b.Scale(1 / (j * j));

            return b;
        }

        public override Matrix GetNMatrixAt(Element targetElement, params double[] isoCoords)
        {
            var xi = isoCoords[0];
            var bar = targetElement as BarElement;
            var l = (bar.Nodes[1].Location - bar.Nodes[0].Location).Length;

            var c0 = bar.NodalReleaseConditions[0];
            var c1 = bar.NodalReleaseConditions[1];

            var order = GetDofOrder(targetElement);

            var d0 = c0.GetComponent(order[0].Dof);
            var r0 = c0.GetComponent(order[1].Dof);

            var d1 = c1.GetComponent(order[2].Dof);
            var r1 = c1.GetComponent(order[3].Dof);

            return GetNMatrixAt(xi, l, d0, r0, d1, r1, Direction);
        }



        /// <summary>
        /// Get the internal defomation of element due to applied <see cref="load"/>
        /// </summary>
        /// <param name="targetElement"></param>
        /// <param name="load"></param>
        /// <param name="isoLocation"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        /// <exception cref="NotImplementedException"></exception>
        public override Displacement GetLoadDisplacementAt(Element targetElement, ElementalLoad load, double[] isoLocation)
        {
            var bar = targetElement as BarElement;

            var n = targetElement.Nodes.Length;

            if (n != 2)
                throw new Exception("more than two nodes not supported");


            if (load is UniformLoad ul)
                return GetLoadDisplacementAt_UniformLoad(bar, ul, isoLocation[0]);

            throw new NotImplementedException();
        }


        private Displacement GetLoadDisplacementAt_UniformLoad(BarElement bar, UniformLoad load, double xi)
        {
            double f0, m0, w0;
            double L;

            if (bar.NodeCount != 2)
                throw new Exception();

            {//step 0
                L = (bar.Nodes[1].Location - bar.Nodes[0].Location).Length;
            }

            #region step 1
            {
                var p0 = GetLocalEquivalentNodalLoads(bar, load)[0];

                p0 = -p0;

                var localDir = load.Direction;

                var tr = bar.GetTransformationManager();

                if(load.CoordinationSystem == CoordinationSystem.Global)
                    localDir = tr.TransformGlobalToLocal(localDir);

                localDir = localDir.GetUnit();

                switch (this.Direction)
                {
                    case BeamDirection.Y:
                        f0 = p0.Fz;
                        m0 = p0.My;//TODO: add possible negative sign
                        w0 = localDir.Z * load.Magnitude;
                        break;

                    case BeamDirection.Z:
                        f0 = p0.Fy;
                        m0 = -p0.Mz;//TODO: add possible negative sign
                        w0 = localDir.Y * load.Magnitude;
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }
            #endregion

            #region step2
            double[] xs;

            {
                var n_ = bar.Section.GetMaxFunctionOrder()[0];
                var m_ = bar.Material.GetMaxFunctionOrder()[0];
                var sn_ = 3 + 2 * n_ + 2 * m_;

                xs = CalcUtil.DivideSpan(0, L, sn_ - 1);
            }
            #endregion

            double[] mOverEis;

            #region step3-4
            {
                var moverEi = new Func<double, double>(x =>
                {
                    var xi_ = (2 * x - L) / L;

                    var mat_ = bar.Material.GetMaterialPropertiesAt(new IsoPoint(xi_, 0, 0), bar);
                    var sec_ = bar.Section.GetCrossSectionPropertiesAt(xi_, bar);

                    var m_x = w0 * x * x / 2 + f0 * x + m0;

                    var e_x = mat_.Ex;
                    var i_x = this.Direction == BeamDirection.Y ? sec_.Iy : sec_.Iz;

                    return m_x / (e_x * i_x);
                });

                mOverEis = xs.Select(x_ => moverEi(x_)).ToArray();
            }
            #endregion


            Polynomial1D G;

            #region step5
            {
                //MathNet.Numerics.LinearAlgebra.
                G = Polynomial1D.FromPoints(xs, mOverEis);
            }
            #endregion

            double delta0, theta0;

            #region step6
            {
                delta0 = theta0 = 0;
            }
            #endregion

            Displacement buf;

            {
                buf = new Displacement();

                var x = (xi + 1) * L / 2;
                //var intg=G.EvaluateNthIntegralAt
                var delta = G.EvaluateNthIntegral(2, x)[0];

                if (Direction == BeamDirection.Y)
                    buf.DZ = delta;

                if (Direction == BeamDirection.Z)
                    buf.DY = delta;
            }

            return buf;
        }

        /// <inheritdoc/>
        public override IEnumerable<Tuple<DoF, double>> GetLoadInternalForceAt(Element targetElement, ElementalLoad load,
            double[] isoLocation)
        {
            var n = targetElement.Nodes.Length;

            var buff = new List<Tuple<DoF, double>>();

            var tr = targetElement.GetTransformationManager();

            var br = targetElement as BarElement;

            var endForces = GetLocalEquivalentNodalLoads(targetElement, load);

            for (var i = 0; i < n; i++)
                endForces[i] = -endForces[i];//(2,1) section

            #region 2,1 (due to inverse of equivalent nodal loads)

            Force ends;//internal force in x=0 due to inverse of equivalent nodal loads will store in this variable, 

            {
                var xi_s = new double[br.Nodes.Length];//xi loc of each force
                var x_s = new double[br.Nodes.Length];//x loc of each force

                for (var i = 0; i < xi_s.Length; i++)
                {
                    var x_i = targetElement.Nodes[i].Location - targetElement.Nodes[0].Location;
                    var xi_i = br.LocalCoordsToIsoCoords(x_i.Length)[0];

                    xi_s[i] = xi_i;
                    x_s[i] = x_i.X;
                }

                ends = new Force();//sum of moved end forces to destination

                for (var i = 0; i < n; i++)
                {
                    if (xi_s[i] < isoLocation[0])
                    {
                        var frc_i = endForces[i];// new Force();

                        ends += frc_i.Move(new Point(x_s[i], 0, 0), Point.Origins);
                    }
                }
            }

            #endregion

            var to = Iso2Local(targetElement, isoLocation)[0];


            #region uniform & trapezoid, uses integration method

            if (load is UniformLoad || load is PartialNonUniformLoad)
            {

                Func<double, double> magnitude;
                Vector localDir;

                double xi0, xi1;
                int degree;//polynomial degree of magnitude function

                #region inits
                if (load is UniformLoad)
                {
                    var uld = load as UniformLoad;

                    magnitude = xi => uld.Magnitude;
                    localDir = uld.Direction;

                    if (uld.CoordinationSystem == CoordinationSystem.Global)
                        localDir = tr.TransformGlobalToLocal(localDir);

                    localDir = localDir.GetUnit();

                    xi0 = -1;
                    xi1 = 1;
                    degree = 0;
                }
                else if (load is PartialNonUniformLoad)
                {
                    var uld = load as PartialNonUniformLoad;

                    magnitude = xi => uld.GetMagnitudeAt(targetElement, new IsoPoint(xi));
                    localDir = uld.Direction;

                    if (uld.CoordinationSystem == CoordinationSystem.Global)
                        localDir = tr.TransformGlobalToLocal(localDir);

                    localDir = localDir.GetUnit();

                    xi0 = uld.StartLocation.Xi;
                    xi1 = uld.EndLocation.Xi;

                    degree = uld.SeverityFunction.Degree[0];
                }
                else
                    throw new NotImplementedException();

                localDir = localDir.GetUnit();
                #endregion

                {

                    var nOrd = 0;// GetNMaxOrder(targetElement).Max();

                    var gpt = (nOrd + degree) / 2 + 3;//gauss point count

                    Matrix integral;

                    double i0 = 0, i1 = 0;//span for integration

                    var xi_t = isoLocation[0];

                    #region span of integration
                    {

                        if (xi_t < xi0)
                        {
                            i0 = i1 = xi0;
                        }

                        if (xi_t > xi1)
                        {
                            i0 = xi0;
                            i1 = xi1;
                        }

                        if (xi_t < xi1 && xi_t > xi0)
                        {
                            i0 = xi0;
                            i1 = xi_t;
                        }
                    }
                    #endregion

                    #region integration
                    {
                        if (i1 == i0)
                        {
                            integral = targetElement.MatrixPool.Allocate(2, 1);
                        }
                        else
                        {
                            var x0 = br.IsoCoordsToLocalCoords(i0)[0];
                            var x1 = br.IsoCoordsToLocalCoords(i1)[0];

                            var intgV = GaussianIntegrator.CreateFor1DProblem(xx =>
                            {
                                //var xi = Local2Iso(targetElement, x)[0];
                                //var j = GetJMatrixAt(targetElement, xi);

                                var xi = br.LocalCoordsToIsoCoords(xx)[0];

                                var q__ = magnitude(xi);
                                var q_ = localDir * q__;

                                double df, dm;

                                if (Direction == BeamDirection.Y)
                                {
                                    df = q_.Z;
                                    dm = -q_.Z * xx;
                                }
                                else
                                {
                                    df = q_.Y;
                                    dm = q_.Y * xx;
                                }

                                var buf_ = targetElement.MatrixPool.Allocate(new double[] { df, dm });

                                return buf_;
                            }, x0, x1, gpt);

                            integral = intgV.Integrate();
                        }
                    }
                    #endregion


                    var v_i = integral[0, 0];//total shear
                    var m_i = integral[1, 0];//total moment of load about the start node

                    var x = Iso2Local(targetElement, isoLocation)[0];

                    var f = new Force();//total moment about start node, total shear 


                    if (Direction == BeamDirection.Y)
                    {
                        f.Fz = v_i;
                        f.My = m_i;//negative is taken into account earlier
                    }
                    else
                    {
                        f.Fy = v_i;
                        f.Mz = m_i;
                    }



                    {
                        //this block is commented to fix the issue #48 on github
                        //when this block is commented out, then issue 48 is fixed
                        /*
                        if (br.StartReleaseCondition.DY == DofConstraint.Released)
                            f.Fy = 0;

                        if (br.StartReleaseCondition.DZ == DofConstraint.Released)
                            f.Fz = 0;

                        if (br.StartReleaseCondition.RY == DofConstraint.Released)
                            f.My = 0;

                        if (br.StartReleaseCondition.RZ == DofConstraint.Released)
                            f.Mz = 0;
                        */
                    }


                    var f2 = f + ends;

                    f2 = f2.Move(new Point(0, 0, 0), new Point(x, 0, 0));

                    f2 *= -1;

                    if (Direction == BeamDirection.Y)
                    {
                        buff.Add(Tuple.Create(DoF.Ry, f2.My));
                        buff.Add(Tuple.Create(DoF.Dz, f2.Fz));
                    }
                    else
                    {
                        buff.Add(Tuple.Create(DoF.Rz, f2.Mz));
                        buff.Add(Tuple.Create(DoF.Dy, f2.Fy));
                    }


                    return buff;
                }
            }



            #endregion

            #region concentrated

            if (load is ConcentratedLoad)
            {
                var cns = load as ConcentratedLoad;

                var xi = isoLocation[0];
                var targetX = br.IsoCoordsToLocalCoords(xi)[0];

                var frc = Force.Zero;

                if (cns.ForceIsoLocation.Xi < xi)
                    frc = cns.Force;

                if (cns.CoordinationSystem == CoordinationSystem.Global)
                    frc = tr.TransformGlobalToLocal(frc);

                var frcX = br.IsoCoordsToLocalCoords(cns.ForceIsoLocation.Xi)[0];

                if (frc != Force.Zero)
                {
                    frc = frc.Move(new Point(frcX, 0, 0), new Point(0, 0, 0));
                    frc = frc.Move(new Point(0, 0, 0), new Point(targetX, 0, 0));
                }

                var movedEnds = ends.Move(new Point(0, 0, 0), new Point(targetX, 0, 0));

                var f2 = frc + movedEnds;
                f2 *= -1;


                if (Direction == BeamDirection.Y)
                {
                    buff.Add(Tuple.Create(DoF.Ry, f2.My));
                    buff.Add(Tuple.Create(DoF.Dz, f2.Fz));
                }
                else
                {
                    buff.Add(Tuple.Create(DoF.Rz, f2.Mz));
                    buff.Add(Tuple.Create(DoF.Dy, f2.Fy));
                }

                return buff;
            }

            #endregion

            throw new NotImplementedException();
        }

      
        /// <inheritdoc/>
        public override Displacement GetLocalDisplacementAt(Element targetElement, Displacement[] localDisplacements, params double[] isoCoords)
        {
            var nc = targetElement.Nodes.Length;

            var ld = localDisplacements;

            Matrix B = targetElement.MatrixPool.Allocate(2, 4);

            var xi = isoCoords[0];

            if (xi < -1 || xi > 1)
                throw new ArgumentOutOfRangeException("isoCoords");

            var bar = targetElement as BarElement;

            if (bar == null)
                throw new Exception();

            var n = GetNMatrixAt(targetElement, isoCoords);

            //var d = GetDMatrixAt(targetElement, isoCoords);

            var u = targetElement.MatrixPool.Allocate(2 * nc, 1);

            var j = GetJMatrixAt(targetElement, isoCoords).Determinant();

            if (Direction == BeamDirection.Y)
            {
                for (int i = 0; i < nc; i++)
                {
                    u[2 * i + 0, 0] = ld[i].DZ;
                    u[2 * i + 1, 0] = ld[i].RY;
                }
            }
            else
            {
                for (int i = 0; i < nc; i++)
                {
                    u[2 * i + 0, 0] = ld[i].DY;
                    u[2 * i + 1, 0] = ld[i].RZ;
                }
            }

            var f = n * u;

            //var ei = d[0, 0];

            f.ScaleRow(1, 1 / j);

            var buf = new Displacement();

            if (Direction == BeamDirection.Y)
            {
                buf.DZ = f[0, 0];
                buf.RY = -f[1, 0];//TODO: Not sure why, but negative should be applied
            }
            else
            {
                buf.DY = f[0, 0];
                buf.RZ = f[1, 0];
            }

            return buf;
        }

        /// <inheritdoc/>
        public override IEnumerable<Tuple<DoF, double>> GetLocalInternalForceAt(Element targetElement, Displacement[] localDisplacements, params double[] isoCoords)
        {
            var nc = targetElement.Nodes.Length;

            var ld = localDisplacements;

            Matrix B = targetElement.MatrixPool.Allocate(2, 4);

            var xi = isoCoords[0];

            if (xi < -1 || xi > 1)
                throw new ArgumentOutOfRangeException("isoCoords");

            var buf = new List<Tuple<DoF, double>>();

            if (Direction == BeamDirection.Y)
            {
                if (localDisplacements.All(i => i.DZ == 0 && i.RY == 0))
                    return buf;
            }
            else
            {
                if (localDisplacements.All(i => i.DY == 0 && i.RZ == 0))
                    return buf;
            }

            var bar = targetElement as BarElement;

            if (bar == null)
                throw new Exception();


            var n = GetNMatrixAt(targetElement, isoCoords);

            var d = GetDMatrixAt(targetElement, isoCoords);

            var u = targetElement.MatrixPool.Allocate(2 * nc, 1);

            var j = GetJMatrixAt(targetElement, isoCoords).Determinant();

            if (Direction == BeamDirection.Y)
            {
                for (int i = 0; i < nc; i++)
                {
                    u[2 * i + 0, 0] = ld[i].DZ;
                    u[2 * i + 1, 0] = ld[i].RY;
                }
            }
            else
            {
                for (int i = 0; i < nc; i++)
                {
                    u[2 * i + 0, 0] = ld[i].DY;
                    u[2 * i + 1, 0] = ld[i].RZ;
                }
            }

            var f = n * u;

            var ei = d[0, 0];

            f.ScaleRow(1, 1 / j);
            f.ScaleRow(2, ei / (j * j));
            f.ScaleRow(3, ei / (j * j * j));

            if (Direction == BeamDirection.Y)
                f.ScaleRow(2, -1);//m/ei = - n''*u , due to where? TODO

            if (Direction == BeamDirection.Y)
            {
                buf.Add(Tuple.Create(DoF.Ry, f[2, 0]));
                buf.Add(Tuple.Create(DoF.Dz, -f[3, 0]));
            }
            else
            {
                buf.Add(Tuple.Create(DoF.Rz, f[2, 0]));
                buf.Add(Tuple.Create(DoF.Dy, -f[3, 0]));
            }

            return buf;
        }


        public override Force[] GetLocalEquivalentNodalLoads(Element targetElement, ElementalLoad load)
        {
            var bar = targetElement as BarElement;
            var n = bar.Nodes.Length;


            //https://www.quora.com/How-should-I-perform-element-forces-or-distributed-forces-to-node-forces-translation-in-the-beam-element

            var tr = targetElement.GetTransformationManager();

            #region uniform 

            if (load is UniformLoad || load is PartialNonUniformLoad)
            {

                Func<double, double> magnitude;
                Vector localDir;

                double xi0, xi1;
                int degree;//polynomial degree of magnitude function

                #region inits
                if (load is UniformLoad)
                {
                    var uld = load as UniformLoad;

                    magnitude = xi => uld.Magnitude;
                    localDir = uld.Direction;

                    if (uld.CoordinationSystem == CoordinationSystem.Global)
                        localDir = tr.TransformGlobalToLocal(localDir);

                    localDir = localDir.GetUnit();

                    xi0 = -1;
                    xi1 = 1;
                    degree = 0;
                }
                else if (load is PartialNonUniformLoad)
                {
                    var uld = load as PartialNonUniformLoad;

                    magnitude = xi => uld.SeverityFunction.Evaluate(xi);
                    localDir = uld.Direction;

                    if (uld.CoordinationSystem == CoordinationSystem.Global)
                        localDir = tr.TransformGlobalToLocal(localDir);

                    localDir = localDir.GetUnit();

                    xi0 = uld.StartLocation.Xi;
                    xi1 = uld.EndLocation.Xi;

                    degree = uld.SeverityFunction.Degree[0];// Coefficients.Length;
                }
                else
                    throw new NotImplementedException();

                localDir = localDir.GetUnit();
                #endregion

                {

                    var nOrd = GetNMaxOrder(targetElement).Max();

                    var gpt = (nOrd + degree) / 2 + 1;//gauss point count

                    var intg = GaussianIntegrator.CreateFor1DProblem(xi =>
                    {
                        var shp = GetNMatrixAt(targetElement, xi, 0, 0);

                        /*
                        if (_direction == BeamDirection.Y)
                        {
                            for (var i = 0; i < shp.ColumnCount; i++)
                            {
                                if (i % 2 == 1)
                                    shp.MultiplyColumnByConstant(i, -1);
                            }
                        }*/

                        var q__ = magnitude(xi);
                        var j = GetJMatrixAt(targetElement, xi, 0, 0);
                        shp.Scale(j.Determinant());

                        var q_ = localDir * q__;

                        if (Direction == BeamDirection.Y)
                            shp.Scale(q_.Z);
                        else
                            shp.Scale(q_.Y);

                        return shp;
                    }, xi0, xi1, gpt);

                    var res = intg.Integrate();

                    if (n > 2)
                        throw new Exception("beam with more than 2 node not supported!");

                    var localForces = new Force[n];



                    if (Direction == BeamDirection.Y)
                    {
                        var fz0 = res[0, 0];
                        var my0 = res[0, 1];
                        var fz1 = res[0, 2];
                        var my1 = res[0, 3];

                        localForces[0] = new Force(0, 0, fz0, 0, my0, 0);
                        localForces[1] = new Force(0, 0, fz1, 0, my1, 0);
                    }
                    else
                    {

                        var fy0 = res[0, 0];
                        var mz0 = res[0, 1];
                        var fy1 = res[0, 2];
                        var mz1 = res[0, 3];

                        localForces[0] = new Force(0, fy0, 0, 0, 0, mz0);
                        localForces[1] = new Force(0, fy1, 0, 0, 0, mz1);
                    }

                    return localForces;
                }
            }

            #endregion

            #region ConcentratedLoad

            if (load is ConcentratedLoad)
            {
                var cl = load as ConcentratedLoad;

                var localforce = cl.Force;

                if (cl.CoordinationSystem == CoordinationSystem.Global)
                    localforce = tr.TransformGlobalToLocal(localforce);

                var buf = new Force[n];

                var ns = GetNMatrixAt(targetElement, cl.ForceIsoLocation.Xi);

                /*
                if (_direction == BeamDirection.Y)
                    for (var i = 0; i < ns.ColumnCount; i++)
                        if (i % 2 == 1)
                            ns.MultiplyColumnByConstant(i, -1);
                */


                var j = GetJMatrixAt(targetElement, cl.ForceIsoLocation.Xi);

                var detJ = j.Determinant();

                ns.ScaleRow(1, 1 / detJ);
                ns.ScaleRow(2, 1 / (detJ * detJ));
                ns.ScaleRow(3, 1 / (detJ * detJ * detJ));


                for (var i = 0; i < n; i++)
                {
                    var node = bar.Nodes[i];

                    var fi = new Force();

                    var ni = ns[0, 2 * i];
                    var mi = ns[0, 2 * i + 1];

                    var nip = ns[1, 2 * i];
                    var mip = ns[1, 2 * i + 1];

                    if (Direction == BeamDirection.Z)
                    {
                        fi.Fy += localforce.Fy * ni;//concentrated force
                        fi.Mz += localforce.Fy * mi;//concentrated force

                        fi.Fy += localforce.Mz * nip;//concentrated moment
                        fi.Mz += localforce.Mz * mip;//concentrated moment
                    }
                    else
                    {
                        fi.Fz += localforce.Fz * ni;//concentrated force
                        fi.My += localforce.Fz * mi;//concentrated force

                        fi.Fz += localforce.My * -nip;//concentrated moment
                        fi.My += localforce.My * -mip;//concentrated moment
                    }

                    buf[i] = fi;
                }

                return buf;
            }




            #endregion



            throw new NotImplementedException();
        }

        public override GeneralStressTensor GetLoadStressAt(Element targetElement, ElementalLoad load, double[] isoLocation)
        {
            throw new NotImplementedException();
        }

        public override GeneralStressTensor GetLocalInternalStressAt(Element targetElement, Displacement[] localDisplacements, params double[] isoCoords)
        {
            throw new NotImplementedException();
        }



        public override DoF[] GetDofsPerNode()
        {
            var d = Direction == BeamDirection.Y ? DoF.Dz : DoF.Dy;
            var r = Direction == BeamDirection.Y ? DoF.Ry : DoF.Rz;

            return new DoF[] { d, r };
        }

        protected override int GetBOrder()
        {
            return 1;
        }

        protected override int GetNOrder()
        {
            return 3;
        }


        public override double GetMu(BarElement targetElement, double xi)
        {
            var geo = targetElement.Section.GetCrossSectionPropertiesAt(xi, targetElement);
            var mat = targetElement.Material.GetMaterialPropertiesAt(xi);

            return mat.Mu * geo.A;
        }

        public override double GetRho(BarElement targetElement, double xi)
        {
            var geo = targetElement.Section.GetCrossSectionPropertiesAt(xi, targetElement);
            var mat = targetElement.Material.GetMaterialPropertiesAt(xi);

            return mat.Rho * geo.A;
        }

        public override double GetD(BarElement targetElement, double xi)
        {
            var geo = targetElement.Section.GetCrossSectionPropertiesAt(xi, targetElement);
            var mech = targetElement.Material.GetMaterialPropertiesAt(xi);

            if (Direction == BeamDirection.Y)
                return geo.Iy * mech.Ex;
            else
                return geo.Iz * mech.Ex;
        }



        #endregion
    }
}
