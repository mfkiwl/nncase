/* Copyright 2019-2021 Canaan Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
#pragma once
#include "../call.h"
#include "../op.h"
#include "nncase/runtime/datatypes.h"
#include "opcode.h"

namespace nncase::ir::nn {
/** @brief Sigmoid operator node */
class NNCASE_API sigmoid_node : public op_node {
  public:
    DEFINE_NODE_OPCODE(op_nn_sigmoid);

    sigmoid_node();
};

/** @brief Sigmoid expression */
class sigmoid : public call {
  public:
    /** @brief Construct a sigmoid expression
     *  @param[in] input The input of the sigmoid
     */
    NNCASE_API sigmoid(expr input);
};
} // namespace nncase::ir::nn