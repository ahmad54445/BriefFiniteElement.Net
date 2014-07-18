﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BriefFiniteElementNet
{
    /// <summary>
    /// Represents the result of linear analysis of structure against defined load combinations
    /// </summary>
    public class StaticLinearAnalysisResult
    {
        private Model parent;
        private Dictionary<LoadCase, double[]> displacements = new Dictionary<LoadCase, double[]>();
        private Dictionary<LoadCase, double[]> forces = new Dictionary<LoadCase, double[]>();
        private LoadCase settlementsLoadCase;

        internal int[] ReleasedMap;             //ReleasedMap[GlobalDofIndex] = DoF index in free DoFs
        internal int[] FixedMap;            //FixedMap[GlobalDofIndex] = DoF index in fixed DoFs
        internal int[] ReversedReleasedMap;     //ReversedReleasedMap[DoF index in free DoFs] = GlobalDofIndex
        internal int[] ReversedFixedMap;    //ReversedFixedMap[DoF index in fixed DoFs] = GlobalDofIndex


        /// <summary>
        /// Adds the analysis result if not exists.
        /// </summary>
        /// <param name="cse">The cse.</param>
        /// <remarks>If current instanse do not contains the results reloated to <see cref="cse"/>, then this method will add result related to <see cref="cse"/> using <see cref="StaticLinearAnalysisResult.AddAnalysisResult"/> method</remarks>
        public void AddAnalysisResultIfNotExists(LoadCase cse)
        {
            if (displacements.ContainsKey(cse))
                return;

            AddAnalysisResult(cse);
        }

        /// <summary>
        /// Gets the displacements.
        /// </summary>
        /// <value>
        /// The displacements of DoFs under each <see cref="LoadCase"/>.
        /// </value>
        /// <remarks>
        /// model under each load case may have different displacements vector for system. the key and value pair in <see cref="Displacements"/> property 
        /// contains the displacement or settlements of DoFs (for released dofs, it is displacement and for constrainted dofs it is settlements)
        /// </remarks>
        public Dictionary<LoadCase, double[]> Displacements
        {
            get { return displacements; }
            internal set { displacements = value; }
        }

        /// <summary>
        /// Gets the forces.
        /// </summary>
        /// <value>
        /// The forces on DoFs under with each <see cref="LoadCase"/>.
        /// </value>
        /// <remarks>
        /// each load case may have different loads vector for system. the key and value pair in <see cref="Forces"/> property 
        /// contains the external load or support reactions (for released dofs, it is external load and for constrainted dofs it is support reaction)
        /// </remarks>
        public Dictionary<LoadCase, double[]> Forces
        {
            get { return forces; }
            internal set { forces = value; }
        }

        internal Model Parent
        {
            get { return parent; }
            set { parent = value; }
        }

        /// <summary>
        /// Gets or sets the settlements load case.
        /// </summary>
        /// <value>
        /// The load case that settlements should be threated
        /// </value>
        internal LoadCase SettlementsLoadCase
        {
            get { return settlementsLoadCase; }
            set { settlementsLoadCase = value; }
        }


        /// <summary>
        /// The cholesky decmposition of Kff, will be used for fast solving the model under new load cases
        /// </summary>
        internal CSparse.Double.Factorization.SparseCholesky KffCholesky;



        internal CSparse.Double.CompressedColumnStorage Kfs;

        internal CSparse.Double.CompressedColumnStorage Kss;

        /// <summary>
        /// Adds the analysis result.
        /// </summary>
        /// <param name="cse">The load case.</param>
        /// <remarks>if model is analysed against specific load case, then displacements are available throgh <see cref="Displacements"/> property.
        /// If system is not analysed against a specific load case, then this method will analyse structure agains <see cref="loadCase"/>.
        /// While this method is using pre computed Cholesky Decomposition (the <see cref="StiffnessMatrixCholeskyDecomposition"/> is meant) , its have a high performance in solving the system.
        /// </remarks>
        public void AddAnalysisResult(LoadCase cse)
        {
            var sp = System.Diagnostics.Stopwatch.StartNew();

            var haveSettlement = false;

            var fixCount = this.Kfs.ColumnCount;
            var freeCount = this.Kfs.RowCount;

            var nodes = parent.Nodes;

            var uf = new double[freeCount];
            var pf = new double[freeCount];

            var us = new double[fixCount];
            var ps = new double[fixCount];

            var n = parent.Nodes.Count;

            #region Initializing Node.MembersLoads

            for (var i = 0; i < n; i++) parent.Nodes[i].MembersLoads.Clear();

            foreach (var elm in parent.Elements)
            {
                var nc = elm.Nodes.Length;

                foreach (var ld in elm.Loads)
                {
                    if (ld.Case != cse)
                        continue;

                    var frc = ld.GetEquivalentNodalLoads(elm);

                    for (int i = 0; i < nc; i++)
                    {
                        elm.Nodes[i].MembersLoads.Add(new NodalLoad(frc[i], cse));
                    }
                }
            }

            #endregion

            TraceUtil.WritePerformanceTrace("Calculating end memeber forces took {0} ms", sp.ElapsedMilliseconds);
            sp.Restart();
            


            var fmap = this.FixedMap;
            var rmap = this.ReleasedMap;


            for (int i = 0; i < n; i++)
            {
                var force = new Force();

                foreach (var ld in nodes[i].MembersLoads)
                    force += ld.Force;


                foreach (var ld in nodes[i].Loads)
                    if (ld.Case == cse)
                        force += ld.Force;


                var cns = nodes[i].Constraints;
                var disp = new Displacement();

                if (cse == this.SettlementsLoadCase) disp = nodes[i].Settlements;


                #region DX

                if (cns.Dx == DofConstraint.Released)
                {
                    pf[rmap[6*i + 0]] = force.Fx;
                    uf[rmap[6*i + 0]] = disp.Dx;
                }
                else
                {
                    ps[fmap[6*i + 0]] = force.Fx;
                    us[fmap[6*i + 0]] = disp.Dx;
                }

                #endregion

                #region DY

                if (cns.Dy == DofConstraint.Released)
                {
                    pf[rmap[6*i + 1]] = force.Fy;
                    uf[rmap[6*i + 1]] = disp.Dy;
                }
                else
                {
                    ps[fmap[6*i + 1]] = force.Fy;
                    us[fmap[6*i + 1]] = disp.Dy;
                }

                #endregion

                #region DZ

                if (cns.Dz == DofConstraint.Released)
                {
                    pf[rmap[6*i + 2]] = force.Fz;
                    uf[rmap[6*i + 2]] = disp.Dz;
                }
                else
                {
                    ps[fmap[6*i + 2]] = force.Fz;
                    us[fmap[6*i + 2]] = disp.Dz;
                }

                #endregion



                #region RX

                if (cns.Rx == DofConstraint.Released)
                {
                    pf[rmap[6*i + 3]] = force.Mx;
                    uf[rmap[6*i + 3]] = disp.Rx;
                }
                else
                {
                    ps[fmap[6*i + 3]] = force.Mx;
                    us[fmap[6*i + 3]] = disp.Rx;
                }

                #endregion

                #region Ry

                if (cns.Ry == DofConstraint.Released)
                {
                    pf[rmap[6*i + 4]] = force.My;
                    uf[rmap[6*i + 4]] = disp.Ry;
                }
                else
                {
                    ps[fmap[6*i + 4]] = force.My;
                    us[fmap[6*i + 4]] = disp.Ry;
                }

                #endregion

                #region Rz

                if (cns.Rz == DofConstraint.Released)
                {
                    pf[rmap[6*i + 5]] = force.Mz;
                    uf[rmap[6*i + 5]] = disp.Rz;
                }
                else
                {
                    ps[fmap[6*i + 5]] = force.Mz;
                    us[fmap[6*i + 5]] = disp.Rz;
                }

                #endregion
            }


            TraceUtil.WritePerformanceTrace("forming Uf,Us,Ff,Fs took {0} ms", sp.ElapsedMilliseconds);
            sp.Restart();



            #region determining that have settlement for better performance

            for (int i = 0; i < fixCount; i++)
                if (us[i] != 0)
                {
                    haveSettlement = true;
                    break;
                }

            #endregion

            #region Solving equation system

            for (int i = 0; i < fixCount; i++)
                ps[i] = 0; //no need existing values

            var tmp=Kfs.RowIndices.Max();
            if (haveSettlement)
            {
                KffCholesky.Solve(MathUtil.ArrayMinus(pf, MathUtil.Muly(Kfs, us)), uf); //uf = kff^-1(Pf-Kfs*us)

                this.Kfs.TransposeMultiply(uf, ps); //ps += Kfs*Uf
                this.Kss.Multiply(us, ps); //ps += Kss*Us
            }
            else
            {
                KffCholesky.Solve(pf, uf); //uf = kff^-1(Pf

                this.Kfs.TransposeMultiply(uf, ps); //ps += Kfs*Uf
            }

            #endregion

            TraceUtil.WritePerformanceTrace("resolving the system with pre computed cholesky decomposition tooks {0} ms", sp.ElapsedMilliseconds);
            sp.Restart();

            var ut = new double[6 * n];
            var ft = new double[6 * n];

            var revFMap = this.ReversedFixedMap;
            var revRMap = this.ReversedReleasedMap;


            for (int i = 0; i < freeCount; i++)
            {
                ut[revRMap[i]] = uf[i];
                ft[revRMap[i]] = pf[i];
            }


            for (int i = 0; i < fixCount; i++)
            {
                ut[revFMap[i]] = us[i];
                ft[revFMap[i]] = ps[i];
            }

            TraceUtil.WritePerformanceTrace("Assembling Ut, Pt from Uf,Ff,Us,Fs tooks {0} ms", sp.ElapsedMilliseconds);
            sp.Restart();

            displacements[cse] = ut;
            forces[cse] = ft;
        }
    }
}